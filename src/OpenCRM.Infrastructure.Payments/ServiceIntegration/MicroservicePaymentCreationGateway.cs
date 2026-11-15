using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Payments.Dto;
using Payments.Contracts;

namespace OpenCRM.Infrastructure.Payments.ServiceIntegration;

/// <summary> Фиксирует покупку и команду одной транзакцией до любого сетевого обращения. </summary>
internal sealed class MicroservicePaymentCreationGateway(
    OpenCRMContext context,
    IOptions<PaymentsServiceOptions> options,
    IPromotionPaymentCoordinator promotions,
    IOrganizationPermissionService permissions,
    TimeProvider clock,
    ILogger<MicroservicePaymentCreationGateway> logger,
    Helpers.ProjectBrand? brand = null) : IPaymentCreationGateway, IPaymentPurchaseQueryService
{
    public async Task<PaymentCreationResult?> ResumeAsync(PaymentCreationIntent intent)
    {
        if (!TryScope(intent, out _) || YooKassaService.ValidateBuyer(intent.Buyer) != null)
        {
            return Invalid();
        }

        var purchase = await this.LoadAsync(intent.OperationId);
        if (purchase == null)
        {
            return null;
        }

        return Matches(purchase, intent) ? Map(purchase) : Conflict();
    }

    public async Task<PaymentPurchaseState?> GetAsync(string userId, Guid purchaseId, CancellationToken cancellationToken = default)
    {
        var purchase = await this.LoadAsync(purchaseId, cancellationToken);
        if (purchase == null || purchase.UserId != userId)
        {
            return null;
        }

        var target = "/subscription";
        if (purchase.OrganizationId.HasValue)
        {
            var scenario = await context.Organizations.AsNoTracking()
                .Where(item => item.Id == purchase.OrganizationId.Value)
                .Select(item => item.PrimaryScenarioCode).SingleOrDefaultAsync(cancellationToken);
            target = scenario == OrganizationTypeCodes.Detailing
                ? $"/detailing/{purchase.OrganizationId:D}#billing"
                : scenario is OrganizationTypeCodes.ServiceStation or "autoservice"
                    ? $"/autoservice/{purchase.OrganizationId:D}#billing" : "/organizations";
        }

        return new PaymentPurchaseState(Map(purchase), purchase.Payment.PaidAt.HasValue, target);
    }

    public async Task<PaymentCreationResult> CreateAsync(PaymentCreationDraft draft)
    {
        if (draft == null || !TryScope(draft.Intent, out var organizationId)
            || YooKassaService.ValidateBuyer(draft.Intent.Buyer) != null
            || draft.SubscriptionPlanId <= 0 || draft.Intent.SubscriptionPlanId.HasValue && draft.Intent.SubscriptionPlanId != draft.SubscriptionPlanId
            || draft.Amount <= 0 || draft.Amount > 1_000_000 || draft.Amount != decimal.Round(draft.Amount, 2)
            || string.IsNullOrWhiteSpace(draft.Description))
        {
            return Invalid();
        }

        if (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction != null
            || System.Transactions.Transaction.Current != null)
        {
            return Invalid();
        }

        var existing = await this.ResumeAsync(draft.Intent);
        if (existing != null)
        {
            return existing;
        }

        if (!options.Value.Enabled || !options.Value.AcceptNewPurchases)
        {
            return new PaymentCreationResult(PaymentCreationStatus.Rejected, "Подготовка новых оплат временно отключена.");
        }

        var resultUrl = string.IsNullOrWhiteSpace(draft.Intent.ReturnUrl) ? options.Value.ReturnUrl : draft.Intent.ReturnUrl;
        // Адрес возврата задаётся настройкой приложения. Пока он не задан, платёжную ссылку создать нельзя,
        // поэтому сообщаем именно об отсутствующей настройке, а не о некорректных параметрах покупки.
        if (string.IsNullOrWhiteSpace(resultUrl))
        {
            return new PaymentCreationResult(PaymentCreationStatus.Rejected, "Адрес возврата после оплаты не настроен.");
        }

        if (resultUrl != options.Value.ReturnUrl && !options.Value.AllowedReturnUrls.Contains(resultUrl, StringComparer.Ordinal)
            || !Uri.TryCreate(resultUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return Invalid();
        }

        var workspacePurchaseType = organizationId.HasValue ? $"yookassa_workspace_purchase:{organizationId:D}" : string.Empty;
        var workspaceExtensionType = organizationId.HasValue ? $"yookassa_workspace_extension:{organizationId:D}" : string.Empty;
        var beneficiaryKey = organizationId.HasValue ? $"organization:{organizationId:D}" : $"personal:{draft.Intent.UserId}";
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({beneficiaryKey}))");
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({draft.Intent.OperationId.ToString("N")}))");
            var previous = await this.LoadAsync(draft.Intent.OperationId);
            if (previous != null)
            {
                await transaction.CommitAsync();

                return Matches(previous, draft.Intent) ? Map(previous) : Conflict();
            }

            if (await context.SubscriptionPurchases.AnyAsync(item => item.BeneficiaryKey == beneficiaryKey && item.IsOpen))
            {
                return new PaymentCreationResult(PaymentCreationStatus.Conflict, "Для этого получателя уже есть незавершённая оплата. Завершите её или обратитесь в поддержку.");
            }

            if (await context.PaymentCreationOperations.AnyAsync(item => item.PaymentProviderCode != "payments-service"
                && (organizationId.HasValue ? item.OperationType == workspacePurchaseType || item.OperationType == workspaceExtensionType
                    : item.UserId == draft.Intent.UserId && (item.OperationType == "yookassa_purchase" || item.OperationType == "yookassa_extension"))
                && (item.Status == PaymentCreationOperationStatuses.Prepared || item.Status == PaymentCreationOperationStatuses.Sending
                    || item.Status == PaymentCreationOperationStatuses.ManualReview
                    || item.SubscriptionPayment != null && item.SubscriptionPayment.PaidAt == null
                        && item.SubscriptionPayment.PaymentStatus != "canceled" && item.SubscriptionPayment.PaymentStatus != "failed"
                        && item.SubscriptionPayment.PaymentStatus != "completed" && item.SubscriptionPayment.PaymentStatus != "refunded")))
            {
                return new PaymentCreationResult(PaymentCreationStatus.Conflict, "Предыдущая оплата ещё не завершена. Новое списание не создаётся.");
            }

            if (await context.PaymentCreationOperations.AnyAsync(item => item.IdempotenceKey == draft.Intent.OperationId.ToString("N")))
            {
                return Conflict();
            }

            var plan = await context.SubscriptionPlans.AsNoTracking().Include(item => item.Features)
                .ThenInclude(item => item.FeatureDefinition).SingleOrDefaultAsync(item => item.Id == draft.SubscriptionPlanId);
            if (plan == null || !plan.IsActive || plan.IsDemoPlan || plan.IsDefaultPlan || plan.DurationDays <= 0
                || plan.PlanTypeCode != (organizationId.HasValue ? SubscriptionPlanCatalogCodes.PlanTypes.Business : SubscriptionPlanCatalogCodes.PlanTypes.Personal)
                || draft.Promotion == null && plan.EffectivePrice != draft.Amount)
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, "Условия тарифа изменились. Обновите страницу перед оплатой.");
            }

            if (draft.Promotion is { } promotion
                && (!organizationId.HasValue || promotion.OrganizationId.HasValue && promotion.OrganizationId != organizationId
                    || promotion.PromoCodeId <= 0 || string.IsNullOrWhiteSpace(promotion.PromoCode)
                    || promotion.OriginalAmount != plan.EffectivePrice || promotion.DiscountAmount <= 0
                    || promotion.OriginalAmount - promotion.DiscountAmount != draft.Amount))
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, "Условия скидки изменились. Обновите тарифы перед оплатой.");
            }

            if (!await this.CanCreateAsync(draft, organizationId, plan))
            {
                return new PaymentCreationResult(PaymentCreationStatus.Invalid, "Условия покупки изменились. Обновите тарифы перед оплатой.");
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var issuedAt = new DateTimeOffset(now, TimeSpan.Zero);
            var buyer = YooKassaService.NormalizeBuyer(draft.Intent.Buyer!);
            var operation = new PaymentCreationOperation
            {
                Id = draft.Intent.OperationId,
                IdempotenceKey = draft.Intent.OperationId.ToString("N"),
                PaymentProviderCode = "payments-service",
                UserId = draft.Intent.UserId,
                OrganizationId = organizationId,
                SubscriptionPlanId = plan.Id,
                OperationType = draft.Intent.OperationType,
                Amount = draft.Amount,
                Description = draft.Description,
                ReturnUrl = resultUrl,
                BuyerTypeCode = buyer.TypeCode,
                BuyerName = buyer.Name,
                BuyerInn = buyer.Inn,
                RequestTimestamp = now,
                CreatedAt = now,
                PromoCodeApplicationId = draft.Promotion?.PromoCodeApplicationId,
                PromoCodeId = draft.Promotion?.PromoCodeId,
                PromoCodeSnapshot = draft.Promotion?.PromoCode,
                OriginalAmount = draft.Promotion?.OriginalAmount,
                PromoDiscountAmount = draft.Promotion?.DiscountAmount
            };
            var payment = new SubscriptionPayment
            {
                UserId = operation.UserId,
                PaymentProviderCode = "payments-service",
                SubscriptionPlanId = plan.Id,
                Amount = draft.Amount,
                CreatedAt = now,
                StatusUpdatedAt = now,
                PaymentMethod = operation.OperationType,
                PaymentStatus = "processing",
                BuyerTypeCode = buyer.TypeCode,
                BuyerName = buyer.Name,
                BuyerInn = buyer.Inn
            };
            operation.SubscriptionPayment = payment;
            context.PaymentCreationOperations.Add(operation);
            await promotions.ClaimAsync(operation);
            await context.SaveChangesAsync();
            await promotions.AttachPaymentAsync(operation, payment);
            var command = new CreatePayment(operation.Id, "opencrm", operation.Id, checked((long)(draft.Amount * 100)), "RUB",
                $"Подписка {brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName}: {plan.Name}"[..Math.Min(128, $"Подписка {brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName}: {plan.Name}".Length)],
                $"{resultUrl}?purchaseId={operation.Id:D}", operation.UserId,
                organizationId?.ToString("D") ?? operation.UserId,
                IssuedAt: issuedAt, NotAfter: issuedAt.AddHours(23), MerchantAccountId: options.Value.MerchantAccountId);
            operation.RequestBody = JsonSerializer.Serialize(command);
            var purchase = new SubscriptionPurchase
            {
                Id = operation.Id,
                UserId = operation.UserId,
                OrganizationId = organizationId,
                BeneficiaryKey = beneficiaryKey,
                CreationOperation = operation,
                Payment = payment,
                PlanSnapshot = SubscriptionPlanSnapshot.Capture(plan),
                AmountMinor = command.AmountMinor,
                IsTest = options.Value.ExpectedIsTest,
                CreatedAt = now,
                UpdatedAt = now,
                NextApplicationAttemptAt = now,
                NextReconcileAt = now.AddMinutes(1)
            };
            context.SubscriptionPurchases.Add(purchase);
            context.SubscriptionPaymentCommands.Add(new SubscriptionPaymentCommand
            {
                Id = operation.Id,
                Purchase = purchase,
                Payload = operation.RequestBody,
                PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operation.RequestBody))),
                CreatedAt = now,
                NextAttemptAt = now
            });
            SubscriptionPurchaseDiagnostics.Add(context, purchase, "checkout", "accepted", now, command.CommandId);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            logger.LogInformation("Покупка подписки сохранена. PurchaseId={PurchaseId}, CommandId={CommandId}, AmountMinor={AmountMinor}, IsTest={IsTest}",
                purchase.Id, command.CommandId, purchase.AmountMinor, purchase.IsTest);

            return Map(purchase);
        });
    }

    private async Task<bool> CanCreateAsync(PaymentCreationDraft draft, Guid? organizationId, SubscriptionPlan plan)
    {
        if (!await context.Users.AnyAsync(user => user.Id == draft.Intent.UserId))
        {
            return false;
        }

        var extension = draft.Intent.OperationType.Contains("extension", StringComparison.Ordinal);
        var now = clock.GetUtcNow().UtcDateTime;
        if (organizationId.HasValue)
        {
            if (!await context.Organizations.AnyAsync(item => item.Id == organizationId && item.IsActive && !item.IsDeleted && !item.IsArchived
                && item.Members.Any(member => member.UserId == draft.Intent.UserId))
                || !await permissions.HasPermissionAsync(draft.Intent.UserId, organizationId.Value, OrganizationPermissionCodes.BillingManage))
            {
                return false;
            }

            var current = await context.OrganizationSubscriptions.AsNoTracking().Include(item => item.SubscriptionPlan)
                .Where(item => item.OrganizationId == organizationId && item.EndDate >= now).OrderByDescending(item => item.EndDate).FirstOrDefaultAsync();
            return extension
                ? current != null && current.SubscriptionPlanId == plan.Id && current.SourceCode != "promo"
                    && !current.SubscriptionPlan.IsDemoPlan && !current.SubscriptionPlan.IsDefaultPlan && current.EndDate <= now.AddDays(7)
                : current == null || current.SourceCode == "promo" || current.SubscriptionPlan.IsDemoPlan || current.SubscriptionPlan.IsDefaultPlan;
        }

        var personal = await context.UserSubscriptions.AsNoTracking().Include(item => item.SubscriptionPlan)
            .Where(item => item.UserId == draft.Intent.UserId && item.EndDate >= now).OrderByDescending(item => item.EndDate).FirstOrDefaultAsync();
        return extension
            ? personal != null && personal.SubscriptionPlanId == plan.Id && !personal.SubscriptionPlan.IsDemoPlan && personal.EndDate <= now.AddDays(7)
            : personal == null || personal.SubscriptionPlanId != plan.Id && plan.Order >= personal.SubscriptionPlan.Order;
    }

    private Task<SubscriptionPurchase?> LoadAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.SubscriptionPurchases.AsNoTracking().Include(item => item.Payment).Include(item => item.CreationOperation)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    private static bool TryScope(PaymentCreationIntent? intent, out Guid? organizationId)
    {
        organizationId = null;
        if (intent == null || intent.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(intent.UserId))
        {
            return false;
        }

        if (intent.OperationType is "yookassa_purchase" or "yookassa_extension")
        {
            return true;
        }

        var parts = intent.OperationType?.Split(':');
        if (parts?.Length != 2 || parts[0] is not ("yookassa_workspace_purchase" or "yookassa_workspace_extension")
            || !Guid.TryParse(parts[1], out var parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        organizationId = parsed;
        return true;
    }

    private static bool Matches(SubscriptionPurchase purchase, PaymentCreationIntent intent)
    {
        var buyer = YooKassaService.NormalizeBuyer(intent.Buyer!);
        var operation = purchase.CreationOperation;

        return purchase.UserId == intent.UserId && operation.OperationType == intent.OperationType
            && (!intent.SubscriptionPlanId.HasValue || operation.SubscriptionPlanId == intent.SubscriptionPlanId)
            && operation.BuyerTypeCode == buyer.TypeCode && operation.BuyerName == buyer.Name && operation.BuyerInn == buyer.Inn
            && (string.IsNullOrWhiteSpace(intent.ReturnUrl) || intent.ReturnUrl == operation.ReturnUrl);
    }

    internal static PaymentCreationResult Map(SubscriptionPurchase purchase)
    {
        var payment = purchase.Payment;
        if (purchase.ApplicationStatus == SubscriptionPurchaseStatuses.NeedsAttention)
        {
            return new(PaymentCreationStatus.ManualReview, "Оплата требует проверки. Повторно платить не нужно; обратитесь в поддержку с номером покупки.", payment.ExternalPaymentId, PurchaseId: purchase.Id);
        }

        if (purchase.RefundedAmountMinor == purchase.AmountMinor)
        {
            return new(PaymentCreationStatus.Refunded, "Оплата возвращена.", payment.ExternalPaymentId, PurchaseId: purchase.Id);
        }

        if (purchase.AppliedAt.HasValue)
        {
            return new(PaymentCreationStatus.Completed, "Подписка активна.", payment.ExternalPaymentId, PurchaseId: purchase.Id);
        }

        if (purchase.ApplicationStatus == SubscriptionPurchaseStatuses.Closed)
        {
            return new(purchase.ProviderStatus == "Canceled" ? PaymentCreationStatus.Canceled : PaymentCreationStatus.Rejected,
                "Оплата не выполнена.", payment.ExternalPaymentId, PurchaseId: purchase.Id);
        }

        if (payment.PaidAt.HasValue)
        {
            return new(PaymentCreationStatus.InProgress, "Оплата получена, активируем подписку.", payment.ExternalPaymentId, PurchaseId: purchase.Id);
        }

        return !string.IsNullOrWhiteSpace(payment.PaymentLink)
            ? new(PaymentCreationStatus.PaymentRequired, "Оплата подготовлена.", payment.ExternalPaymentId, payment.PaymentLink, purchase.Id)
            : new(PaymentCreationStatus.InProgress, "Готовим оплату. Повторно платить не нужно.", PurchaseId: purchase.Id);
    }

    private static PaymentCreationResult Invalid() => new(PaymentCreationStatus.Invalid, "Параметры покупки некорректны.");
    private static PaymentCreationResult Conflict() => new(PaymentCreationStatus.Conflict, "Ключ оплаты уже используется с другими параметрами.");
}
