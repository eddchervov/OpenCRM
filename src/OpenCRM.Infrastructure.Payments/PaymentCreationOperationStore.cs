using System.Data;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Infrastructure.Payments;

/// <summary> Хранилище операций создания платежей. </summary>
internal sealed class PaymentCreationOperationStore(OpenCRMContext context, IPromotionPaymentCoordinator? promotionPaymentCoordinator = null)
{
    /// <summary> Длительность аренды операции. </summary>
    private static readonly TimeSpan s_leaseDuration = TimeSpan.FromMinutes(2);

    /// <summary> Время жизни операции. </summary>
    private static readonly TimeSpan s_operationLifetime = TimeSpan.FromHours(23);

    /// <summary> Подготавливает операцию создания платежа с проверкой идемпотентности. </summary>
    public async Task<PaymentCreationOperation> PrepareAsync(PaymentCreationOperation operation, bool hasRequestTimestamp)
    {
        this.EnsureCleanUnitOfWork();
        this.DetachTrackedOperation(operation.IdempotenceKey);
        this.DetachTrackedPromotion(operation.Id);
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            this.DetachTrackedOperation(operation.IdempotenceKey);
            this.DetachTrackedPromotion(operation.Id);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var workspace = operation.OperationType.Split(':');
            var beneficiaryKey = workspace.Length == 2 && Guid.TryParse(workspace[1], out var workspaceId)
                ? $"organization:{workspaceId:D}" : $"personal:{operation.UserId}";
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({beneficiaryKey}))");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({operation.IdempotenceKey}))");

            var existing = await context.PaymentCreationOperations
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.IdempotenceKey == operation.IdempotenceKey);

            if (existing != null)
            {
                if (!this.HasSameRequest(existing, operation, hasRequestTimestamp))
                {
                    throw new PaymentCreationConflictException();
                }

                await transaction.CommitAsync();
                return existing;
            }

            if (await context.SubscriptionPurchases.AnyAsync(item => item.BeneficiaryKey == beneficiaryKey && item.IsOpen))
            {
                throw new PaymentCreationConflictException();
            }

            context.PaymentCreationOperations.Add(operation);

            // Захват заявки промокода выполняется в той же транзакции под идемпотентной блокировкой.
            if (operation.PromoCodeApplicationId.HasValue)
            {
                await this.GetPromotionCoordinator().ClaimAsync(operation);
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            return operation;
        });
    }

    /// <summary> Получает аренду операции для обработки. </summary>
    public async Task<PaymentCreationLease?> AcquireLeaseAsync(Guid operationId)
    {
        this.EnsureCleanUnitOfWork();
        this.DetachTrackedOperation(operationId);
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            this.DetachTrackedOperation(operationId);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var operation = await context.PaymentCreationOperations
                .FromSqlInterpolated($"SELECT * FROM \"PaymentCreationOperations\" WHERE \"Id\" = {operationId} FOR UPDATE")
                .SingleOrDefaultAsync();

            if (operation == null || operation.PaymentProviderCode == "payments-service")
            {
                await transaction.CommitAsync();
                return null;
            }

            var now = DateTime.UtcNow;
            if (operation.Status == PaymentCreationOperationStatuses.Prepared
                || operation.Status == PaymentCreationOperationStatuses.Sending && operation.LeaseUntil <= now)
            {
                if (operation.FirstAttemptAt.HasValue && operation.FirstAttemptAt.Value < now - PaymentCreationOperationStore.s_operationLifetime)
                {
                    operation.Status = PaymentCreationOperationStatuses.ManualReview;
                    operation.LastErrorCode = "operation_expired";
                    operation.LeaseToken = null;
                    operation.LeaseUntil = null;
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return null;
                }

                operation.Status = PaymentCreationOperationStatuses.Sending;
                operation.FirstAttemptAt ??= now;
                operation.LastAttemptAt = now;
                operation.LeaseToken = Guid.NewGuid();
                operation.LeaseUntil = now.Add(PaymentCreationOperationStore.s_leaseDuration);
                operation.AttemptCount++;
                await context.SaveChangesAsync();
                var lease = PaymentCreationLease.FromOperation(operation);
                await transaction.CommitAsync();
                return lease;
            }

            await transaction.CommitAsync();
            return null;
        });
    }

    /// <summary> Завершает операцию успешным ответом провайдера. </summary>
    public async Task<PaymentResponse?> FinalizeSuccessAsync(PaymentCreationLease lease, YooKassaPaymentResponse response)
    {
        this.EnsureCleanUnitOfWork();
        this.DetachTrackedOperation(lease.OperationId);
        this.DetachTrackedPayment(response.Id);
        this.DetachTrackedPromotion(lease.OperationId);
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            this.DetachTrackedOperation(lease.OperationId);
            this.DetachTrackedPayment(response.Id);
            this.DetachTrackedPromotion(lease.OperationId);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var operation = await context.PaymentCreationOperations
                .FromSqlInterpolated($"SELECT * FROM \"PaymentCreationOperations\" WHERE \"Id\" = {lease.OperationId} FOR UPDATE")
                .SingleOrDefaultAsync();

            if (operation == null)
            {
                await transaction.CommitAsync();
                return PaymentResponse.CreateError("Не удалось сохранить операцию создания платежа.");
            }

            if (!PaymentCreationOperationStore.HasMatchingLease(operation, lease))
            {
                await transaction.CommitAsync();
                return PaymentResponse.CreateError("Платеж требует ручной проверки.");
            }

            if (operation.Status == PaymentCreationOperationStatuses.Created)
            {
                var responseResult = operation.ExternalPaymentId == response.Id
                    ? PaymentResponse.CreateSuccess(operation.PaymentLink, operation.ExternalPaymentId)
                    : await this.MarkManualReviewAsync(operation, "provider_payment_id_conflict");

                await transaction.CommitAsync();

                return responseResult;
            }

            if (operation.Status is not (PaymentCreationOperationStatuses.Prepared or PaymentCreationOperationStatuses.Sending))
            {
                await transaction.CommitAsync();

                return PaymentResponse.CreateError("Платеж требует ручной проверки.");
            }

            if (operation.ExternalPaymentId != null && operation.ExternalPaymentId != response.Id)
            {
                var conflict = await this.MarkManualReviewAsync(operation, "provider_payment_id_conflict");
                await transaction.CommitAsync();

                return conflict;
            }

            var existingPayment = await context.SubscriptionPayments.AsNoTracking()
                .FirstOrDefaultAsync(payment => payment.ExternalPaymentId == response.Id);

            if (existingPayment != null && existingPayment.Id != operation.SubscriptionPaymentId)
            {
                var conflict = await this.MarkManualReviewAsync(operation, "provider_payment_id_conflict");
                await transaction.CommitAsync();

                return conflict;
            }

            var payment = await context.SubscriptionPayments
                .FirstOrDefaultAsync(item => item.Id == operation.SubscriptionPaymentId);

            if (payment == null)
            {
                payment = new SubscriptionPayment
                {
                    UserId = operation.UserId,
                    SubscriptionPlanId = operation.SubscriptionPlanId,
                    Amount = operation.Amount,
                    OriginalAmount = operation.OriginalAmount,
                    PromoDiscountAmount = operation.PromoDiscountAmount,
                    PromoCodeId = operation.PromoCodeId,
                    PromoCodeSnapshot = operation.PromoCodeSnapshot,
                    PromoCodeApplicationId = operation.PromoCodeApplicationId,
                    CreatedAt = response.CreatedAt == default ? operation.CreatedAt : response.CreatedAt,
                    StatusUpdatedAt = DateTime.UtcNow,
                    PaymentMethod = operation.OperationType,
                    PaymentStatus = "pending",
                    TransactionId = response.Id,
                    PaymentLink = response.Confirmation?.ConfirmationUrl,
                    ExternalPaymentId = response.Id,
                    BuyerTypeCode = operation.BuyerTypeCode,
                    BuyerName = operation.BuyerTypeCode == "business" ? operation.BuyerName?.Trim() : null,
                    BuyerInn = operation.BuyerTypeCode == "business" ? operation.BuyerInn?.Trim() : null
                };
                context.SubscriptionPayments.Add(payment);
            }

            operation.Status = PaymentCreationOperationStatuses.Created;
            operation.ExternalPaymentId = response.Id;
            operation.PaymentLink = response.Confirmation?.ConfirmationUrl;
            operation.ProviderCreatedAt = response.CreatedAt == default ? null : response.CreatedAt;
            operation.SubscriptionPayment = payment;
            operation.LeaseToken = null;
            operation.LeaseUntil = null;
            operation.LastErrorCode = null;

            // Привязка платежа к заявке выполняется до сохранения: идентификатор платежа может быть ещё нулевым.
            if (operation.PromoCodeApplicationId.HasValue)
            {
                await this.GetPromotionCoordinator().AttachPaymentAsync(operation, payment);
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            return PaymentResponse.CreateSuccess(operation.PaymentLink, operation.ExternalPaymentId);
        });
    }

    /// <summary> Записывает ошибку выполнения операции. </summary>
    public async Task RecordFailureAsync(PaymentCreationLease lease, string status, string errorCode)
    {
        this.EnsureCleanUnitOfWork();
        this.DetachTrackedOperation(lease.OperationId);
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            this.DetachTrackedOperation(lease.OperationId);
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var operation = await context.PaymentCreationOperations
                .FromSqlInterpolated($"SELECT * FROM \"PaymentCreationOperations\" WHERE \"Id\" = {lease.OperationId} FOR UPDATE")
                .SingleOrDefaultAsync();

            if (operation == null || operation.LeaseToken != lease.LeaseToken
                || operation.Status != PaymentCreationOperationStatuses.Sending)
            {
                await transaction.CommitAsync();
                return;
            }

            operation.Status = status;
            operation.LastErrorCode = errorCode;
            operation.LeaseToken = null;
            operation.LeaseUntil = null;
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        });
    }

    /// <summary> Возвращает идентификаторы операций, готовых к повторной попытке. </summary>
    public async Task<IReadOnlyList<Guid>> GetRetryableOperationIdsAsync(int limit)
    {
        var now = DateTime.UtcNow;

        return await context.PaymentCreationOperations.AsNoTracking()
            .Where(operation => operation.PaymentProviderCode != "payments-service")
            .Where(operation => operation.Status == PaymentCreationOperationStatuses.Prepared
                || operation.Status == PaymentCreationOperationStatuses.Sending && operation.LeaseUntil <= now)
            .OrderBy(operation => operation.CreatedAt)
            .Select(operation => operation.Id)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary> Отбрасывает отслеживаемое состояние операции. </summary>
    public void DiscardTrackedState(Guid operationId, string externalPaymentId)
    {
        this.DetachTrackedOperation(operationId);
        this.DetachTrackedPayment(externalPaymentId);
        this.DetachTrackedPromotion(operationId);
    }

    /// <summary> Помечает операцию как требующую ручного разбора. </summary>
    private async Task<PaymentResponse> MarkManualReviewAsync(
        PaymentCreationOperation operation,
        string errorCode)
    {
        operation.Status = PaymentCreationOperationStatuses.ManualReview;
        operation.LastErrorCode = errorCode;
        operation.LeaseToken = null;
        operation.LeaseUntil = null;
        await context.SaveChangesAsync();

        return PaymentResponse.CreateError("Платеж требует ручной проверки.");
    }

    /// <summary> Проверяет совпадение запроса с существующей операцией. </summary>
    private bool HasSameRequest(PaymentCreationOperation existing, PaymentCreationOperation incoming, bool hasRequestTimestamp) =>
        existing.UserId == incoming.UserId
        && existing.SubscriptionPlanId == incoming.SubscriptionPlanId
        && existing.OperationType == incoming.OperationType
        && existing.Amount == incoming.Amount
        && existing.OriginalAmount == incoming.OriginalAmount
        && existing.PromoDiscountAmount == incoming.PromoDiscountAmount
        && existing.PromoCodeId == incoming.PromoCodeId
        && existing.PromoCodeSnapshot == incoming.PromoCodeSnapshot
        && existing.PromoCodeApplicationId == incoming.PromoCodeApplicationId
        && existing.OrganizationId == incoming.OrganizationId
        && existing.Description == incoming.Description
        && existing.ReturnUrl == incoming.ReturnUrl
        && existing.BuyerTypeCode == incoming.BuyerTypeCode
        && existing.BuyerName == incoming.BuyerName
        && existing.BuyerInn == incoming.BuyerInn
        && (!hasRequestTimestamp || existing.RequestTimestamp == incoming.RequestTimestamp);

    /// <summary> Проверяет соответствие аренды операции. </summary>
    private static bool HasMatchingLease(PaymentCreationOperation operation, PaymentCreationLease lease) =>
        operation.Id == lease.OperationId
        && operation.IdempotenceKey == lease.IdempotenceKey
        && operation.RequestBody == lease.RequestBody
        && operation.UserId == lease.UserId
        && operation.SubscriptionPlanId == lease.PlanId
        && operation.Amount == lease.Amount
        && operation.RequestTimestamp == lease.RequestTimestamp;

    /// <summary> Возвращает координатор промо-платежей. </summary>
    private IPromotionPaymentCoordinator GetPromotionCoordinator() =>
        promotionPaymentCoordinator
            ?? throw new InvalidOperationException("Операция с заявкой промокода требует зарегистрированного координатора промокода.");

    /// <summary> Отсоединяет отслеживаемую операцию по ключу идемпотентности. </summary>
    private void DetachTrackedOperation(string idempotenceKey)
    {
        foreach (var entry in context.ChangeTracker.Entries<PaymentCreationOperation>()
            .Where(entry => entry.Entity.IdempotenceKey == idempotenceKey)
            .ToArray())
        {
            this.DetachOperationAndPayment(entry);
        }
    }

    /// <summary> Отсоединяет отслеживаемую операцию по идентификатору. </summary>
    private void DetachTrackedOperation(Guid operationId)
    {
        foreach (var entry in context.ChangeTracker.Entries<PaymentCreationOperation>()
            .Where(entry => entry.Entity.Id == operationId)
            .ToArray())
        {
            this.DetachOperationAndPayment(entry);
        }
    }

    /// <summary> Отсоединяет операцию и связанный платёж от контекста. </summary>
    private void DetachOperationAndPayment(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<PaymentCreationOperation> entry)
    {
        if (entry.Entity.SubscriptionPayment != null)
        {
            context.Entry(entry.Entity.SubscriptionPayment).State = EntityState.Detached;
        }
        else if (entry.Entity.SubscriptionPaymentId.HasValue)
        {
            var paymentEntry = context.ChangeTracker.Entries<SubscriptionPayment>()
                .FirstOrDefault(item => item.Entity.Id == entry.Entity.SubscriptionPaymentId.Value);

            if (paymentEntry != null)
            {
                paymentEntry.State = EntityState.Detached;
            }
        }

        entry.State = EntityState.Detached;
    }

    /// <summary> Отсоединяет отслеживаемый платёж по внешнему идентификатору. </summary>
    private void DetachTrackedPayment(string externalPaymentId)
    {
        foreach (var entry in context.ChangeTracker.Entries<SubscriptionPayment>()
            .Where(entry => entry.Entity.ExternalPaymentId == externalPaymentId)
            .ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary> Отсоединяет отслеживаемую промо-акцию операции. </summary>
    private void DetachTrackedPromotion(Guid operationId)
    {
        // Отвязываем только заявки данной операции, изменённые предыдущей неудачной попыткой,
        // чтобы повторная стратегия перечитала актуальные данные из базы.
        foreach (var entry in context.ChangeTracker.Entries<PromoCodeApplication>()
            .Where(entry => entry.Entity.PaymentCreationOperationId == operationId
                && entry.State == EntityState.Modified)
            .ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary> Проверяет отсутствие несохранённых изменений в контексте. </summary>
    private void EnsureCleanUnitOfWork()
    {
        if (System.Transactions.Transaction.Current != null
            || context.Database.CurrentTransaction != null
            || context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("Создание платежа требует отдельной единицы работы без внешней транзакции и ожидающих изменений.");
        }
    }
}

/// <summary> Аренда операции создания платежа. </summary>
internal sealed record PaymentCreationLease(
    Guid OperationId,
    Guid LeaseToken,
    string IdempotenceKey,
    string RequestBody,
    string UserId,
    int PlanId,
    decimal Amount,
    DateTime RequestTimestamp)
{
    /// <summary> Создаёт аренду из операции создания платежа. </summary>
    public static PaymentCreationLease FromOperation(PaymentCreationOperation operation) =>
        new(
            operation.Id,
            operation.LeaseToken!.Value,
            operation.IdempotenceKey,
            operation.RequestBody,
            operation.UserId,
            operation.SubscriptionPlanId,
            operation.Amount,
            operation.RequestTimestamp);
}

/// <summary> Исключение конфликта при создании платежа. </summary>
internal sealed class PaymentCreationConflictException : Exception;
