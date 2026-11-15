using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Payments.Contracts;
using Payments.Contracts;

namespace OpenCRM.Infrastructure.Payments.ServiceIntegration;

/// <summary> Принимает идемпотентные поручения администратора до обращения к сети. </summary>
internal sealed class SubscriptionPaymentAdminService(
    OpenCRMContext context,
    IOptions<PaymentsServiceOptions> options,
    TimeProvider clock,
    ILogger<SubscriptionPaymentAdminService> logger) : IAdminPaymentServiceActions
{
    public async Task<AdminPaymentServiceActions?> GetAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        var purchase = await context.SubscriptionPurchases.AsNoTracking().Include(item => item.Payment)
            .SingleOrDefaultAsync(item => item.SubscriptionPaymentId == paymentId, cancellationToken);
        if (purchase == null || purchase.Payment.PaymentProviderCode != "payments-service")
        {
            return null;
        }

        return await this.DescribeAsync(purchase, cancellationToken);
    }

    public async Task<AdminPaymentServiceActionResult> ExecuteAsync(int paymentId, AdminPaymentServiceActionRequest request,
        string actorUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await this.ExecuteCoreAsync(paymentId, request, actorUserId, cancellationToken);
            if (result.StatusCode >= 400)
            {
                logger.LogWarning("Действие администратора отклонено. PaymentId={PaymentId}, OperationId={OperationId}, Actor={Actor}, Action={Action}, Status={Status}, Code={Code}",
                    paymentId, request.OperationId, actorUserId, request.Action is "check" or "refund" or "retry-access" or "resolve-review" ? request.Action : "invalid", result.StatusCode, result.Code ?? "action_rejected");
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            SubscriptionPurchaseDiagnostics.Failure(logger, "admin_action_commit", null, exception);
            throw;
        }
    }

    private async Task<AdminPaymentServiceActionResult> ExecuteCoreAsync(int paymentId, AdminPaymentServiceActionRequest request,
        string actorUserId, CancellationToken cancellationToken = default)
    {
        if (request.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(actorUserId)
            || actorUserId.Length > 450 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500
            || request.StateToken?.Length != 64 || request.Action is not ("check" or "refund" or "retry-access" or "resolve-review"))
        {
            return new(null, 400, "Укажите действие, причину и актуальное состояние карточки.");
        }

        if (request.Action == "refund" && (request.Amount == null || request.Amount <= 0
            || request.Amount > long.MaxValue / 100m || decimal.Round(request.Amount.Value, 2) != request.Amount))
        {
            return new(null, 400, "Укажите положительную сумму возврата с точностью до копейки.");
        }

        if (request.Action != "refund" && request.Amount != null)
        {
            return new(null, 400, "Сумма указывается только для возврата.");
        }

        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { paymentId, request, actorUserId }))));

        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({"admin-action:" + request.OperationId.ToString("N")}))", cancellationToken);
            var previous = await context.SubscriptionPaymentAdminActions.SingleOrDefaultAsync(item => item.Id == request.OperationId, cancellationToken);
            if (previous != null)
            {
                await transaction.CommitAsync(cancellationToken);

                return previous.RequestHash == requestHash
                    ? new AdminPaymentServiceActionResult(ToDto(previous), previous.CompletedAt.HasValue ? 200 : 202)
                    : new(null, 409, "Этот номер операции уже использован с другими параметрами. Не повторяйте денежную операцию с новым номером до проверки результата.", "operation_id_conflict");
            }

            var purchaseId = await context.SubscriptionPurchases.Where(item => item.SubscriptionPaymentId == paymentId)
                .Select(item => (Guid?)item.Id).SingleOrDefaultAsync(cancellationToken);
            if (purchaseId == null)
            {
                return new AdminPaymentServiceActionResult(null, 404, "Покупка платёжного сервиса не найдена.");
            }

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({"purchase:" + purchaseId.Value.ToString("N")}))", cancellationToken);
            var purchase = await context.SubscriptionPurchases.Include(item => item.Payment)
                .SingleAsync(item => item.Id == purchaseId, cancellationToken);
            var state = await this.DescribeAsync(purchase, cancellationToken);
            if (purchase.Payment.PaymentProviderCode != "payments-service" || state.StateToken != request.StateToken)
            {
                return new AdminPaymentServiceActionResult(null, 409, "Платёж изменился. Обновите карточку и проверьте суммы перед подтверждением.");
            }

            var allowed = request.Action switch
            {
                "check" => state.CanCheck,
                "refund" => state.CanRefund && request.Amount <= state.RefundableAmount,
                "retry-access" => state.CanRetryAccess,
                _ => state.CanResolveReview
            };
            if (!allowed)
            {
                var message = request.Action switch
                {
                    "check" => state.CheckHint,
                    "refund" when request.Amount > state.RefundableAmount => "Сумма превышает доступный остаток для возврата.",
                    "refund" => state.RefundHint,
                    "retry-access" => state.RetryAccessHint,
                    _ => state.ResolveReviewHint
                };

                return new AdminPaymentServiceActionResult(null, 409, message);
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var operation = new SubscriptionPaymentAdminAction
            {
                Id = request.OperationId,
                PurchaseId = purchase.Id,
                Action = request.Action,
                ActorUserId = actorUserId,
                Reason = request.Reason.Trim(),
                AmountMinor = request.Amount.HasValue ? checked((long)(request.Amount.Value * 100m)) : null,
                RefundedBeforeMinor = purchase.RefundedAmountMinor,
                RequestHash = requestHash,
                StateToken = state.StateToken,
                CreatedAt = now,
                NextAttemptAt = now,
                Status = request.Action == "resolve-review" ? "succeeded" : "queued",
                CompletedAt = request.Action == "resolve-review" ? now : null,
                Code = request.Action == "resolve-review" ? "review_recorded" : "accepted"
            };
            if (request.Action == "refund")
            {
                var create = await context.SubscriptionPaymentCommands.AsNoTracking()
                    .SingleAsync(item => item.PurchaseId == purchase.Id && item.Kind == "create", cancellationToken);
                var original = JsonSerializer.Deserialize<CreatePayment>(create.Payload);
                if (original == null || original.MerchantAccountId != options.Value.MerchantAccountId)
                {
                    return new AdminPaymentServiceActionResult(null, 409, "Магазин покупки не совпадает с текущим подключением. Передайте платёж специалисту.");
                }

                var providerActor = actorUserId.Length <= 128 ? actorUserId
                    : "admin-sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actorUserId)));
                var payload = JsonSerializer.Serialize(new RequestRefund(operation.Id, options.Value.ApplicationId, purchase.Id,
                    operation.AmountMinor!.Value, TrimProviderReason(operation.Reason), providerActor, IssuedAt: new(now, TimeSpan.Zero),
                    NotAfter: new DateTimeOffset(now, TimeSpan.Zero).AddHours(23), MerchantAccountId: original.MerchantAccountId));
                context.SubscriptionPaymentCommands.Add(new SubscriptionPaymentCommand
                {
                    Id = operation.Id, PurchaseId = purchase.Id, Kind = "refund", Payload = payload,
                    PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
                    CreatedAt = now, NextAttemptAt = now
                });
            }

            context.SubscriptionPaymentAdminActions.Add(operation);
            SubscriptionPurchaseDiagnostics.Add(context, purchase, "admin_action", request.Action.Replace('-', '_') + "_requested", now, operation.Id);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Действие администратора принято. PaymentId={PaymentId}, PurchaseId={PurchaseId}, OperationId={OperationId}, Action={Action}, Actor={Actor}, AmountMinor={AmountMinor}",
                paymentId, purchase.Id, operation.Id, operation.Action, actorUserId, operation.AmountMinor);

            return new AdminPaymentServiceActionResult(ToDto(operation), operation.CompletedAt.HasValue ? 200 : 202);
        });
    }

    internal async Task<AdminPaymentServiceActions> DescribeAsync(SubscriptionPurchase purchase, CancellationToken cancellationToken)
    {
        var actions = await context.SubscriptionPaymentAdminActions.AsNoTracking().Where(item => item.PurchaseId == purchase.Id)
            .OrderByDescending(item => item.CreatedAt).ToListAsync(cancellationToken);
        var token = await StateTokenAsync(context, purchase, cancellationToken);
        var resolved = actions.Any(item => item.Action == "resolve-review" && item.Status == "succeeded" && item.StateToken == token);
        var connected = options.Value.Enabled && purchase.IsTest == options.Value.ExpectedIsTest;
        var pendingRefund = actions.Any(item => item.Action == "refund" && item.Status is not ("succeeded" or "failed"));
        var refundCommands = await context.SubscriptionPaymentCommands.AsNoTracking()
            .Where(item => item.PurchaseId == purchase.Id && item.Kind == "refund").Select(item => item.Id).ToListAsync(cancellationToken);
        var unknownRefund = refundCommands.Any(id => actions.All(item => item.Id != id));
        var pendingCheck = actions.Any(item => item.Action == "check" && item.CompletedAt == null);
        var rejectedWithoutMoneyAt = await this.GetRetryableRefundRejectionAsync(purchase, actions, cancellationToken);
        var fresh = actions.Any(item => item.Action == "check" && item.Status == "succeeded"
            && item.CompletedAt >= clock.GetUtcNow().UtcDateTime.AddMinutes(-5) && item.VerifiedStateToken == token
            && (rejectedWithoutMoneyAt == null || item.CompletedAt >= rejectedWithoutMoneyAt));
        var moneyConflict = rejectedWithoutMoneyAt == null && purchase.ApplicationStatus == SubscriptionPurchaseStatuses.NeedsAttention
            && purchase.LastErrorCode is not (null or "refund_requires_access_review" or "access_transaction_failed" or "beneficiary_organization_unavailable" or "beneficiary_user_unavailable" or "subscription_changed_after_checkout" or "paid_subscription_changed_after_checkout");
        var confirmed = purchase.ProviderStatus == "Succeeded" && purchase.Payment.PaidAt != null && purchase.ProviderOrderId != null;
        var refundable = Math.Max(0, (purchase.AmountMinor - purchase.RefundedAmountMinor) / 100m);
        var retryable = purchase.ApplicationStatus == SubscriptionPurchaseStatuses.ActivationPending
            || purchase.ApplicationStatus == SubscriptionPurchaseStatuses.NeedsAttention && purchase.LastErrorCode == "access_transaction_failed";
        var canCheck = connected && purchase.ProviderOrderId.HasValue && purchase.ProviderStatus != "Canceled" && !pendingCheck;
        var canRefund = connected && confirmed && refundable > 0 && !pendingRefund && !unknownRefund && !moneyConflict && fresh;
        var canRetry = connected && confirmed && fresh && !pendingRefund && !unknownRefund && !moneyConflict
            && purchase.AppliedAt == null && purchase.RefundedAmountMinor == 0 && retryable
            && !actions.Any(item => item.Action == "retry-access" && item.CompletedAt == null);
        var needsReview = purchase.ApplicationStatus == SubscriptionPurchaseStatuses.NeedsAttention || purchase.RefundedAmountMinor > 0;
        var checkHint = !connected ? "Подключение платёжного сервиса выключено или режим покупки не совпадает."
            : pendingCheck ? "Проверка уже запрошена. Обновите данные, чтобы увидеть результат."
            : purchase.ProviderStatus == "Canceled" ? "Платёж окончательно отменён провайдером. Повторная оплата не создаётся."
            : !purchase.ProviderOrderId.HasValue ? "Платёж ещё не принят МС. Повторная проверка не создаёт новую оплату."
            : "Запрашивает актуальное состояние в ЮKassa через платёжный сервис. Деньги не списывает; результат появится после проверки.";
        var refundHint = pendingRefund || unknownRefund ? "Предыдущий возврат ещё не завершён или требует проверки. Новый возврат заблокирован до известного результата."
            : !connected ? "Подключение платёжного сервиса выключено."
            : !confirmed ? "Возврат доступен только после подтверждения оплаты."
            : refundable == 0 ? "Вся сумма платежа уже возвращена."
            : moneyConflict ? "Сначала требуется технический разбор противоречия в платеже."
            : !fresh ? "Сначала нажмите «Проверить платёж» и дождитесь свежего результата; он действует 5 минут."
            : rejectedWithoutMoneyAt.HasValue ? "Предыдущий возврат окончательно отклонён до денежной операции. Можно создать новый возврат с новым номером; выданный доступ сохранится."
            : "Возвращает указанную сумму через ЮKassa. Уже выданный доступ сохраняется; его изменение — отдельное действие.";
        var retryHint = !connected ? "Подключение платёжного сервиса выключено или режим покупки не совпадает."
            : purchase.AppliedAt != null ? "Доступ по покупке уже выдан. Повторно период не добавляется."
            : purchase.RefundedAmountMinor > 0 || pendingRefund || unknownRefund ? "После возврата или при неизвестном результате возврата автоматическая выдача запрещена."
            : !fresh ? "Сначала проверьте платёж и дождитесь свежего подтверждения оплаты."
            : !canRetry ? "Повтор доступен только для подтверждённой оплаты с технической ошибкой выдачи, без денежных и тарифных конфликтов."
            : "Повторяет только выдачу уже оплаченного доступа. Не списывает деньги и не продлевает период дважды.";

        var actorIds = actions.Take(20).Select(item => item.ActorUserId).Distinct().ToArray();
        var actors = await context.Users.AsNoTracking().Where(item => actorIds.Contains(item.Id))
            .Select(item => new { item.Id, Name = item.Email ?? item.UserName ?? item.Id })
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);

        return new(token, canCheck, canRefund, canRetry, needsReview && !resolved, refundable, checkHint, refundHint, retryHint,
            !needsReview ? "У этого платежа нет открытого вопроса для разбора."
                : resolved ? "Решение по текущему состоянию уже сохранено. Новое изменение платежа снова потребует разбора."
                : "Сохраняет ваше решение и комментарий. Деньги и доступ не меняет; технические ограничения остаются в силе.",
            resolved, actions.Take(20).Select(item => ToDto(item) with { Actor = actors.GetValueOrDefault(item.ActorUserId, item.ActorUserId) }).ToArray());
    }

    private static string TrimProviderReason(string reason)
    {
        if (reason.Length <= 128)
        {
            return reason;
        }

        // Граница UTF-16 не должна разделять пару суррогатов, например эмодзи в причине возврата.
        var length = char.IsHighSurrogate(reason[127]) && char.IsLowSurrogate(reason[128]) ? 127 : 128;

        return reason[..length];
    }

    private async Task<DateTime?> GetRetryableRefundRejectionAsync(SubscriptionPurchase purchase,
        IReadOnlyList<SubscriptionPaymentAdminAction> actions, CancellationToken cancellationToken)
    {
        if (purchase.LastErrorCode is not ("command_expired" or "payments_disabled" or "invalid_refund"))
        {
            return null;
        }

        var rejection = await context.SubscriptionPaymentEvents.AsNoTracking()
            .Where(item => item.PurchaseId == purchase.Id && item.Kind == "command_rejected" && item.ProcessedAt != null)
            .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Select(item => new { item.CommandId, item.Code, item.CreatedAt }).FirstOrDefaultAsync(cancellationToken);
        var action = actions.SingleOrDefault(item => item.Id == rejection?.CommandId && item.Action == "refund"
            && item.Status == "failed" && item.CompletedAt != null);
        if (action == null || rejection!.Code != purchase.LastErrorCode)
        {
            return null;
        }

        var latestConflict = await context.SubscriptionPaymentEvents.AsNoTracking()
            .Where(item => item.PurchaseId == purchase.Id && (item.Kind == "attention" || item.Kind == "inbox_conflict"))
            .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Select(item => new { item.Kind, item.Code, item.CreatedAt }).FirstOrDefaultAsync(cancellationToken);
        if (latestConflict == null || latestConflict.Kind != "attention" || latestConflict.Code != rejection.Code
            || latestConflict.CreatedAt != rejection.CreatedAt)
        {
            return null;
        }

        var command = await context.SubscriptionPaymentCommands.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == action.Id && item.PurchaseId == purchase.Id && item.Kind == "refund", cancellationToken);
        if (command == null || Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.Payload))) != command.PayloadHash
            || await context.SubscriptionPaymentEvents.AnyAsync(item => item.PurchaseId == purchase.Id && item.CommandId == action.Id
                && item.Kind.StartsWith("refund_operation_"), cancellationToken))
        {
            return null;
        }

        RequestRefund? payload;
        try
        {
            payload = JsonSerializer.Deserialize<RequestRefund>(command.Payload);
        }
        catch (JsonException)
        {
            return null;
        }

        var expectedActor = action.ActorUserId.Length <= 128 ? action.ActorUserId
            : "admin-sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(action.ActorUserId)));
        if (payload == null || payload.SchemaVersion != 2 || payload.CommandId != action.Id || payload.PurchaseId != purchase.Id
            || payload.ApplicationId != options.Value.ApplicationId || payload.AmountMinor != action.AmountMinor
            || payload.Reason != action.Reason && payload.Reason != TrimProviderReason(action.Reason)
            || payload.RequestedBy != action.ActorUserId && payload.RequestedBy != expectedActor)
        {
            return null;
        }

        // Эти два отказа сохраняют квитанцию дедупликации МС до создания операции провайдера.
        if (rejection.Code == "payments_disabled"
            || rejection.Code == "command_expired" && payload.NotAfter != default && payload.NotAfter <= clock.GetUtcNow())
        {
            return action.CompletedAt;
        }

        // invalid_refund может означать временное рассогласование часов; само по себе оно не доказывает отказ навсегда.
        var invalidImmutableFields = string.IsNullOrWhiteSpace(payload.Reason) || payload.Reason.Length > 128
            || string.IsNullOrWhiteSpace(payload.RequestedBy) || payload.RequestedBy.Length > 128;

        return rejection.Code == "invalid_refund" && invalidImmutableFields ? action.CompletedAt : null;
    }

    internal static async Task<string> StateTokenAsync(OpenCRMContext context, SubscriptionPurchase purchase, CancellationToken cancellationToken)
    {
        var latestConflict = await context.SubscriptionPaymentEvents.AsNoTracking()
            .Where(item => item.PurchaseId == purchase.Id && (item.Kind == "attention" || item.Kind == "inbox_conflict"))
            .OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StateToken(purchase) + latestConflict)));
    }

    private static string StateToken(SubscriptionPurchase purchase) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new
        {
            purchase.Id, purchase.ProviderOrderId, purchase.ProviderVersion, purchase.ProviderStatus, purchase.CreationStatus,
            purchase.AmountMinor, purchase.Currency, purchase.RefundedAmountMinor, purchase.AppliedAt, purchase.ApplicationStatus,
            purchase.LastErrorCode, purchase.Payment.PaidAt, purchase.IsTest
        }))));

    internal static AdminPaymentServiceAction ToDto(SubscriptionPaymentAdminAction action) => new(action.Id, action.Action,
        action.Status, action.Code, Message(action), action.AmountMinor / 100m, action.CreatedAt, action.CompletedAt, action.ActorUserId, action.Reason);

    private static string Message(SubscriptionPaymentAdminAction action) => (action.Action, action.Status) switch
    {
        ("resolve-review", _) => "Решение сохранено. Денежные факты и доступ не изменены.",
        ("refund", "succeeded") => "Возврат подтверждён платёжным сервисом. Выданный доступ сохранён.",
        ("refund", "failed") => "Возврат отклонён. Проверьте причину в журнале перед новым запросом.",
        ("refund", "needs_attention") when action.Code == "refund_outcome_unknown" => "Подтверждения возврата пока нет. Сервис проверяет результат; не создавайте новый возврат.",
        ("refund", "needs_attention") => "Результат возврата требует проверки. Новый возврат заблокирован.",
        ("refund", _) => "Возврат принят в обработку. Деньги ещё не считаются возвращёнными; повторный запрос не нужен.",
        ("check", "succeeded") => "Платёж сверён с ЮKassa через платёжный сервис.",
        ("check", "failed") => "Проверка не выполнена. Денежные факты не изменены.",
        ("check", "needs_attention") => "Проверка не завершена. Сервис или провайдер недоступен; попытка будет повторена.",
        ("check", _) => "Проверка запрошена. Дождитесь подтверждения нового снимка от провайдера.",
        ("retry-access", "succeeded") => "Доступ по покупке выдан. Повторного продления не было.",
        ("retry-access", "failed") => "Выдача доступа требует разбора. Деньги повторно не списывались.",
        _ => "Повтор выдачи доступа поставлен в очередь."
    };
}
