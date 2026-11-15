using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payments.Service.Configuration;
using Payments.Service.Domain;
using Payments.Service.Persistence;
using Payments.Service.Providers;

namespace Payments.Service.Processing;

public sealed class PaymentProcessor(
    PaymentDbContext dbContext,
    IPaymentProvider provider,
    PaymentEventPublisher eventPublisher,
    IOptions<PaymentOptions> options,
    TimeProvider timeProvider,
    ILogger<PaymentProcessor> logger)
{
    private static readonly TimeSpan s_leaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan s_pendingCheckInterval = TimeSpan.FromSeconds(30);

    public async Task ProcessDueWorkAsync(CancellationToken cancellationToken)
    {
        await this.ProcessDueOperationsAsync(cancellationToken);
        await this.ProcessDueReconciliationsAsync(cancellationToken);
        await this.ProcessDueWebhooksAsync(cancellationToken);
    }

    public async Task ProcessDueOperationsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var operationIds = await dbContext.ProviderOperations.AsNoTracking()
            .Where(operation => (operation.Status == OperationStatus.Prepared
                    || operation.Status == OperationStatus.OutcomeUnknown
                    || operation.Status == OperationStatus.AwaitingProvider
                    || operation.Status == OperationStatus.Sending && operation.LeaseUntil <= now)
                && operation.NextAttemptAt <= now)
            .OrderBy(operation => operation.NextAttemptAt)
            .Select(operation => operation.Id)
            .Take(5)
            .ToListAsync(cancellationToken);

        foreach (var operationId in operationIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await this.ProcessOperationSafelyAsync(operationId, cancellationToken);
        }
    }

    public async Task ProcessDueReconciliationsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var orderIds = await dbContext.PaymentOrders.AsNoTracking()
            .Where(order => order.ProviderPaymentId != null
                && order.Status != PaymentStatus.Canceled
                && order.NextCheckAt <= now)
            .OrderBy(order => order.NextCheckAt)
            .ThenBy(order => order.Id)
            .Select(order => order.Id)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var orderId in orderIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await this.ReconcilePaymentAsync(orderId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError("Не удалось сверить платёж {PaymentOrderId}; ошибка {ErrorType}.",
                    orderId, exception.GetType().Name);
            }
        }
    }

    public async Task ProcessDueWebhooksAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var webhookIds = await dbContext.WebhookNotifications.AsNoTracking()
            .Where(notification => notification.ProcessedAt == null && notification.NextAttemptAt <= now)
            .OrderBy(notification => notification.NextAttemptAt)
            .Select(notification => notification.Id)
            .Take(5)
            .ToListAsync(cancellationToken);

        foreach (var webhookId in webhookIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await this.ProcessWebhookAsync(webhookId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError("Не удалось обработать уведомление {WebhookId}; ошибка {ErrorType}.",
                    webhookId, exception.GetType().Name);
                await this.ScheduleWebhookRetryAsync(webhookId, "webhook_processing_failed", cancellationToken);
            }
        }
    }

    public async Task ProcessOperationAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (this.IsDisabled())
        {
            return;
        }

        var lease = await this.TryAcquireLeaseAsync(operationId, cancellationToken);
        if (lease is null)
        {
            return;
        }

        try
        {
            await this.SendOperationAsync(lease, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PaymentProviderException exception)
        {
            logger.LogError("Ошибка провайдера {PaymentOrderId} {OperationId} {ErrorCode} {ErrorType}.",
                lease.PaymentOrderId, lease.OperationId, exception.Code, exception.GetType().Name);
            await this.RecordOperationFailureAsync(lease, exception.Code, exception.IsDefinitive,
                exception.RequiresManualReview, cancellationToken, exception.GetType().Name);
        }
        catch (Exception exception)
        {
            logger.LogError("Исход операции {OperationId} неизвестен; ошибка {ErrorType}.",
                operationId, exception.GetType().Name);
            await this.RecordOperationFailureAsync(lease, "provider_outcome_unknown", isDefinitive: false,
                requiresManualReview: false, cancellationToken, exception.GetType().Name);
        }
    }

    public async Task ReconcilePaymentAsync(Guid paymentOrderId, CancellationToken cancellationToken)
    {
        if (this.IsDisabled())
        {
            return;
        }

        _ = await this.ReconcilePaymentCoreAsync(paymentOrderId, cancellationToken);
    }

    private async Task<bool> ReconcilePaymentCoreAsync(Guid paymentOrderId, CancellationToken cancellationToken)
    {
        this.ClearTrackedEntities();
        var order = await dbContext.PaymentOrders.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == paymentOrderId, cancellationToken);
        if (order?.ProviderPaymentId is not { } providerPaymentId)
        {
            return false;
        }

        ProviderPayment payment;
        try
        {
            payment = await provider.GetPaymentAsync(providerPaymentId, cancellationToken);
        }
        catch (PaymentProviderException exception)
        {
            await this.RecordReconciliationFailureAsync(paymentOrderId, exception.Code,
                exception.GetType().Name, cancellationToken);

            return false;
        }

        return await this.ApplyProviderPaymentAsync(paymentOrderId, payment, cancellationToken);
    }

    public async Task ProcessWebhookAsync(Guid webhookId, CancellationToken cancellationToken)
    {
        if (this.IsDisabled())
        {
            return;
        }

        var notification = await dbContext.WebhookNotifications.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == webhookId, cancellationToken);
        if (notification is null || notification.ProcessedAt is not null)
        {
            return;
        }

        if (notification.EventType.StartsWith("payment.", StringComparison.Ordinal))
        {
            var paymentOrderId = await dbContext.PaymentOrders.AsNoTracking()
                .Where(order => order.ProviderPaymentId == notification.ObjectId)
                .Select(order => (Guid?)order.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (paymentOrderId is null)
            {
                await this.ScheduleWebhookRetryAsync(webhookId, "payment_not_linked_yet", cancellationToken);

                return;
            }

            if (!await this.ReconcilePaymentCoreAsync(paymentOrderId.Value, cancellationToken))
            {
                await this.ScheduleWebhookRetryAsync(webhookId, "payment_not_verified", cancellationToken);

                return;
            }

            await this.MarkWebhookProcessedAsync(webhookId, cancellationToken);

            return;
        }

        if (notification.EventType == "refund.succeeded")
        {
            await this.ProcessRefundWebhookAsync(notification, cancellationToken);

            return;
        }

        await this.MarkWebhookProcessedAsync(webhookId, cancellationToken);
    }

    private async Task SendOperationAsync(OperationLease lease, CancellationToken cancellationToken)
    {
        this.ClearTrackedEntities();
        var operation = await dbContext.ProviderOperations.AsNoTracking()
            .Include(item => item.PaymentOrder)
            .SingleOrDefaultAsync(item => item.Id == lease.OperationId, cancellationToken);
        if (operation is null)
        {
            return;
        }

        if (operation.PaymentOrder.IsTest != options.Value.IsTest
            || operation.PaymentOrder.MerchantAccountId != options.Value.MerchantAccountId
            || operation.ExecutionMode != options.Value.Mode
            || operation.ExecutionMode == "Disabled")
        {
            var code = operation.ExecutionMode == options.Value.Mode && operation.ExecutionMode != "Disabled"
                ? "payment_merchant_configuration_mismatch"
                : "payment_execution_mode_mismatch";
            await this.RecordOperationFailureAsync(lease, code,
                isDefinitive: false, requiresManualReview: true, cancellationToken);

            return;
        }

        if (operation.Kind == OperationKind.CreatePayment)
        {
            var payment = operation.ProviderObjectId is { Length: > 0 } existingPaymentId
                ? await provider.GetPaymentAsync(existingPaymentId, cancellationToken)
                : await this.CanPostWithLeaseAsync(lease, cancellationToken)
                    ? await provider.CreatePaymentAsync(operation.RequestBody, operation.IdempotenceKey, cancellationToken)
                    : null;
            if (payment is null)
            {
                return;
            }

            if (!PaymentStateRules.IsValid(payment, operation.PaymentOrder, options.Value.IsTest))
            {
                await this.RecordOperationFailureAsync(lease, "provider_payment_mismatch", isDefinitive: false, requiresManualReview: true, cancellationToken);

                return;
            }

            await this.FinalizePaymentOperationAsync(lease, payment, cancellationToken);

            return;
        }

        if (operation.Kind == OperationKind.Refund)
        {
            var refund = operation.ProviderObjectId is { Length: > 0 } existingRefundId
                ? await provider.GetRefundAsync(existingRefundId, cancellationToken)
                : await this.CanPostWithLeaseAsync(lease, cancellationToken)
                    ? await provider.CreateRefundAsync(operation.RequestBody, operation.IdempotenceKey, cancellationToken)
                    : null;
            if (refund is null)
            {
                return;
            }

            if (!this.IsValidRefund(refund, operation))
            {
                await this.RecordOperationFailureAsync(lease, "provider_refund_mismatch", isDefinitive: false, requiresManualReview: true, cancellationToken);

                return;
            }

            if (refund.Status is not ("succeeded" or "canceled"))
            {
                await this.SetOperationAwaitingProviderAsync(lease, refund.Id, refund.Status, cancellationToken);

                return;
            }

            await this.FinalizeRefundOperationAsync(lease, refund, operation.PaymentOrderId, cancellationToken);
        }
    }

    private async Task<OperationLease?> TryAcquireLeaseAsync(Guid operationId, CancellationToken cancellationToken)
    {
        this.ClearTrackedEntities();
        var now = timeProvider.GetUtcNow();
        var orderId = await dbContext.ProviderOperations.AsNoTracking()
            .Where(operation => operation.Id == operationId)
            .Select(operation => (Guid?)operation.PaymentOrderId)
            .SingleOrDefaultAsync(cancellationToken);
        if (orderId is null)
        {
            return null;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.PaymentOrders.FromSqlInterpolated(
            $"SELECT * FROM \"payments\".\"PaymentOrders\" WHERE \"Id\" = {orderId} FOR UPDATE")
            .LoadAsync(cancellationToken);
        var operation = await dbContext.ProviderOperations
            .FromSqlInterpolated($"SELECT * FROM \"payments\".\"ProviderOperations\" WHERE \"Id\" = {operationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (operation is null || operation.PaymentOrderId != orderId)
        {
            await transaction.CommitAsync(cancellationToken);

            return null;
        }
        if (operation.Status is OperationStatus.Resolved or OperationStatus.Rejected or OperationStatus.ManualReview
            || operation.Status == OperationStatus.Sending && operation.LeaseUntil > now
            || operation.NextAttemptAt > now)
        {
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        var postDeadlineError = this.GetPostDeadlineError(operation, now);
        if (postDeadlineError is not null)
        {
            var order = await dbContext.PaymentOrders.SingleAsync(item => item.Id == operation.PaymentOrderId, cancellationToken);
            operation.PaymentOrder = order;
            await this.MarkManualReviewAsync(operation, postDeadlineError, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        operation.FirstAttemptAt ??= now;
        operation.AttemptCount++;
        operation.LeaseToken = Guid.NewGuid();
        operation.LeaseUntil = now.Add(s_leaseDuration);
        operation.Status = OperationStatus.Sending;
        operation.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        var lease = new OperationLease(operation.Id, operation.PaymentOrderId, operation.Kind, operation.LeaseToken.Value);
        await transaction.CommitAsync(cancellationToken);

        return lease;
    }

    private async Task<bool> CanPostWithLeaseAsync(OperationLease lease, CancellationToken cancellationToken)
    {
        var operation = await dbContext.ProviderOperations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == lease.OperationId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (operation is null
            || operation.ExecutionMode != options.Value.Mode
            || operation.ExecutionMode == "Disabled"
            || operation.Status != OperationStatus.Sending
            || operation.LeaseToken != lease.LeaseToken
            || operation.LeaseUntil <= now
            || operation.ProviderObjectId is not null)
        {
            return false;
        }

        var postDeadlineError = this.GetPostDeadlineError(operation, now);
        if (postDeadlineError is not null)
        {
            await this.RecordOperationFailureAsync(
                lease,
                postDeadlineError,
                isDefinitive: false,
                requiresManualReview: true,
                cancellationToken);

            return false;
        }

        return true;
    }

    private string? GetPostDeadlineError(ProviderOperation operation, DateTimeOffset now)
    {
        if (operation.ProviderObjectId is not null
            || operation.Kind is not (OperationKind.CreatePayment or OperationKind.Refund))
        {
            return null;
        }

        if (operation.PostBefore is null)
        {
            return "provider_post_deadline_missing";
        }

        if (operation.PostBefore <= now)
        {
            return "provider_post_deadline_expired";
        }

        var retryDeadline = now - TimeSpan.FromHours(options.Value.RetryWindowHours);
        if (operation.FirstAttemptAt is not null && operation.FirstAttemptAt <= retryDeadline)
        {
            return "provider_idempotence_window_expired";
        }

        return null;
    }

    private async Task FinalizePaymentOperationAsync(
        OperationLease lease,
        ProviderPayment payment,
        CancellationToken cancellationToken)
    {
        this.ClearTrackedEntities();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var operation = await this.LoadLeasedOperationAsync(lease, cancellationToken);
        if (operation is null)
        {
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        var order = operation.PaymentOrder;
        if (!PaymentStateRules.IsValid(payment, order, options.Value.IsTest))
        {
            await this.MarkManualReviewAsync(operation, "provider_payment_mismatch", cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        var nextStatus = PaymentStateRules.ParseStatus(payment.Status);
        if (nextStatus == PaymentStatus.WaitingForCapture)
        {
            operation.Status = OperationStatus.ManualReview;
            operation.ProviderObjectId = payment.Id;
            operation.ProviderStatus = payment.Status;
            operation.LeaseToken = null;
            operation.LeaseUntil = null;
            operation.LastError = "capture_required_for_new_payment";
            operation.UpdatedAt = timeProvider.GetUtcNow();
            order.ProviderPaymentId = payment.Id;
            order.Status = nextStatus;
            order.LastError = operation.LastError;
            order.NextCheckAt = PaymentReconciliationSchedule.GetNextCheckAt(order, timeProvider.GetUtcNow());
            this.AddAuditEntry(order, operation, "payment_provider_result", operation.LastError, "ProviderStatus");
            logger.LogError("Оплата ожидает ручного завершения {PurchaseId} {PaymentOrderId} {OperationId} {CommandId} {Version} {Attempt} {ErrorCode}.",
                order.PurchaseId, order.Id, operation.Id, operation.CommandId, order.Version,
                operation.AttemptCount, operation.LastError);
            await dbContext.SaveChangesAsync(cancellationToken);
            await eventPublisher.PublishStateAsync(order, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        if (!PaymentStateRules.CanApply(order.Status, nextStatus)
            || payment.RefundedAmountMinor < order.RefundedAmountMinor
            || order.ProviderPaymentId is { } existingProviderId && existingProviderId != payment.Id)
        {
            await this.MarkManualReviewAsync(operation, "provider_payment_state_conflict", cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        var changed = order.Status != nextStatus || order.ProviderPaymentId != payment.Id
            || order.ConfirmationUrl != payment.ConfirmationUrl
            || order.RefundedAmountMinor != payment.RefundedAmountMinor
            || operation.Status != OperationStatus.Resolved;
        operation.Status = OperationStatus.Resolved;
        operation.ProviderObjectId = payment.Id;
        operation.ProviderStatus = payment.Status;
        operation.LeaseToken = null;
        operation.LeaseUntil = null;
        operation.LastError = null;
        operation.UpdatedAt = timeProvider.GetUtcNow();
        order.ProviderPaymentId = payment.Id;
        order.ConfirmationUrl = payment.ConfirmationUrl;
        order.Status = nextStatus;
        if (nextStatus == PaymentStatus.Succeeded)
        {
            order.SucceededAt ??= timeProvider.GetUtcNow();
        }

        order.RefundedAmountMinor = payment.RefundedAmountMinor;
        order.LastError = null;
        order.LastReconciledAt = timeProvider.GetUtcNow();
        order.NextCheckAt = PaymentReconciliationSchedule.GetNextCheckAt(order, timeProvider.GetUtcNow());
        this.AddAuditEntry(order, operation, "payment_provider_result", $"payment_{nextStatus.ToString().ToLowerInvariant()}", null);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (changed)
        {
            await eventPublisher.PublishStateAsync(order, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> ApplyProviderPaymentAsync(
        Guid paymentOrderId,
        ProviderPayment payment,
        CancellationToken cancellationToken)
    {
        this.ClearTrackedEntities();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var order = await dbContext.PaymentOrders
            .FromSqlInterpolated($"SELECT * FROM \"payments\".\"PaymentOrders\" WHERE \"Id\" = {paymentOrderId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            await transaction.CommitAsync(cancellationToken);

            return false;
        }

        if (!PaymentStateRules.IsValid(payment, order, options.Value.IsTest))
        {
            order.ReconciliationFailureCount++;
            order.NextCheckAt = timeProvider.GetUtcNow().Add(this.GetRetryDelay(order.ReconciliationFailureCount));
            this.AddAuditEntry(order, null, "reconciliation", "provider_payment_mismatch", "ProviderResponseMismatch");
            logger.LogError("Ответ провайдера не прошёл проверку {PaymentOrderId} {PurchaseId} {Version} {ErrorCode}.",
                order.Id, order.PurchaseId, order.Version, "provider_payment_mismatch");
            if (order.LastError != "provider_payment_mismatch")
            {
                order.LastError = "provider_payment_mismatch";
                await dbContext.SaveChangesAsync(cancellationToken);
                await eventPublisher.PublishStateAsync(order, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return false;
        }

        var status = PaymentStateRules.ParseStatus(payment.Status);
        if (!PaymentStateRules.CanApply(order.Status, status)
            || payment.RefundedAmountMinor < order.RefundedAmountMinor
            || order.ProviderPaymentId != payment.Id)
        {
            order.ReconciliationFailureCount++;
            order.NextCheckAt = timeProvider.GetUtcNow().Add(this.GetRetryDelay(order.ReconciliationFailureCount));
            this.AddAuditEntry(order, null, "reconciliation", "provider_payment_state_conflict", "ProviderStateConflict");
            logger.LogError("Состояние провайдера конфликтует с оплатой {PaymentOrderId} {PurchaseId} {Version} {ErrorCode}.",
                order.Id, order.PurchaseId, order.Version, "provider_payment_state_conflict");
            if (order.LastError != "provider_payment_state_conflict")
            {
                order.LastError = "provider_payment_state_conflict";
                await dbContext.SaveChangesAsync(cancellationToken);
                await eventPublisher.PublishStateAsync(order, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return false;
        }

        var changed = order.Status != status || order.RefundedAmountMinor != payment.RefundedAmountMinor;
        order.Status = status;
        if (status == PaymentStatus.Succeeded)
        {
            order.SucceededAt ??= timeProvider.GetUtcNow();
        }

        order.RefundedAmountMinor = payment.RefundedAmountMinor;
        order.LastError = null;
        order.ReconciliationFailureCount = 0;
        order.LastReconciledAt = timeProvider.GetUtcNow();
        order.NextCheckAt = PaymentReconciliationSchedule.GetNextCheckAt(order, timeProvider.GetUtcNow());
        if (changed)
        {
            this.AddAuditEntry(order, null, "reconciliation_result",
                $"payment_{status.ToString().ToLowerInvariant()}", null);
        }

        if (changed)
        {
            await eventPublisher.PublishStateAsync(order, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    private async Task ProcessRefundWebhookAsync(WebhookNotification notification, CancellationToken cancellationToken)
    {
        ProviderRefund refund;
        try
        {
            refund = await provider.GetRefundAsync(notification.ObjectId, cancellationToken);
        }
        catch (PaymentProviderException exception)
        {
            await this.ScheduleWebhookRetryAsync(notification.Id, exception.Code, cancellationToken);

            return;
        }

        var orderId = await dbContext.PaymentOrders.AsNoTracking()
            .Where(order => order.ProviderPaymentId == refund.PaymentId)
            .Select(order => (Guid?)order.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (orderId is null)
        {
            await this.ScheduleWebhookRetryAsync(notification.Id, "refund_payment_not_linked", cancellationToken);

            return;
        }

        var payment = await provider.GetPaymentAsync(refund.PaymentId, cancellationToken);
        if (!await this.ApplyProviderPaymentAsync(orderId.Value, payment, cancellationToken))
        {
            await this.ScheduleWebhookRetryAsync(notification.Id, "refund_payment_not_verified", cancellationToken);

            return;
        }

        await this.MarkWebhookProcessedAsync(notification.Id, cancellationToken);
    }

    private async Task FinalizeRefundOperationAsync(
        OperationLease lease,
        ProviderRefund refund,
        Guid paymentOrderId,
        CancellationToken cancellationToken)
    {
        if (lease.Kind != OperationKind.Refund)
        {
            return;
        }

        this.ClearTrackedEntities();
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var operation = await this.LoadLeasedOperationAsync(lease, cancellationToken);
            if (operation is null)
            {
                await transaction.CommitAsync(cancellationToken);

                return;
            }

            operation.ProviderObjectId = refund.Id;
            operation.ProviderStatus = refund.Status;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        this.ClearTrackedEntities();
        try
        {
            var payment = await provider.GetPaymentAsync(refund.PaymentId, cancellationToken);
            await this.FinalizeRefundAndPaymentAsync(lease, refund, payment, paymentOrderId, cancellationToken);
        }
        catch (PaymentProviderException exception)
        {
            await this.RecordRefundConfirmationRetryAsync(lease, refund, exception.Code, cancellationToken);
        }
    }

    private async Task FinalizeRefundAndPaymentAsync(
        OperationLease lease,
        ProviderRefund refund,
        ProviderPayment payment,
        Guid paymentOrderId,
        CancellationToken cancellationToken)
    {
        this.ClearTrackedEntities();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var order = await dbContext.PaymentOrders
            .FromSqlInterpolated($"SELECT * FROM \"payments\".\"PaymentOrders\" WHERE \"Id\" = {paymentOrderId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        var operation = await dbContext.ProviderOperations
            .FromSqlInterpolated($"SELECT * FROM \"payments\".\"ProviderOperations\" WHERE \"Id\" = {lease.OperationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (order is null || operation is null || operation.LeaseToken != lease.LeaseToken)
        {
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        if (!this.IsValidRefund(refund, operation, order.ProviderPaymentId)
            || operation.PaymentOrderId != order.Id
            || !PaymentStateRules.IsValid(payment, order, options.Value.IsTest)
            || !PaymentStateRules.CanApply(order.Status, PaymentStateRules.ParseStatus(payment.Status))
            || payment.RefundedAmountMinor < order.RefundedAmountMinor)
        {
            operation.Status = OperationStatus.OutcomeUnknown;
            operation.ProviderObjectId = refund.Id;
            operation.ProviderStatus = refund.Status;
            operation.LastError = "provider_refund_not_verified";
            operation.UpdatedAt = timeProvider.GetUtcNow();
            operation.LeaseToken = null;
            operation.LeaseUntil = null;
            operation.NextAttemptAt = timeProvider.GetUtcNow().Add(this.GetRetryDelay(operation.AttemptCount));
            order.LastError = operation.LastError;
            this.AddAuditEntry(order, operation, "refund_confirmation", operation.LastError, "ProviderResponseMismatch");
            logger.LogError("Возврат не подтверждён {PaymentOrderId} {OperationId} {CommandId} {Version} {Attempt} {ErrorCode}.",
                order.Id, operation.Id, operation.CommandId, order.Version, operation.AttemptCount,
                operation.LastError);
            await dbContext.SaveChangesAsync(cancellationToken);
            await eventPublisher.PublishOperationStateAsync(order, operation, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        var paymentStatus = PaymentStateRules.ParseStatus(payment.Status);
        var stateChanged = order.Status != paymentStatus || order.RefundedAmountMinor != payment.RefundedAmountMinor;
        order.Status = paymentStatus;
        if (paymentStatus == PaymentStatus.Succeeded)
        {
            order.SucceededAt ??= timeProvider.GetUtcNow();
        }

        order.RefundedAmountMinor = payment.RefundedAmountMinor;
        order.LastError = null;
        order.LastReconciledAt = timeProvider.GetUtcNow();
        order.NextCheckAt = PaymentReconciliationSchedule.GetNextCheckAt(order, timeProvider.GetUtcNow());
        operation.Status = OperationStatus.Resolved;
        operation.ProviderObjectId = refund.Id;
        operation.ProviderStatus = refund.Status;
        operation.LeaseToken = null;
        operation.LeaseUntil = null;
        operation.LastError = refund.Status == "canceled" ? "refund_canceled" : null;
        operation.UpdatedAt = timeProvider.GetUtcNow();
        this.AddAuditEntry(order, operation, "refund_result",
            refund.Status == "canceled" ? "refund_canceled" : "refund_succeeded", null);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (stateChanged)
        {
            await eventPublisher.PublishStateAsync(order, cancellationToken);
        }

        await eventPublisher.PublishOperationStateAsync(order, operation, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SetOperationAwaitingProviderAsync(
        OperationLease lease,
        string providerObjectId,
        string providerStatus,
        CancellationToken cancellationToken)
    {
        this.ClearTrackedEntities();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var operation = await this.LoadLeasedOperationAsync(lease, cancellationToken);
        if (operation is null)
        {
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        operation.Status = OperationStatus.AwaitingProvider;
        operation.ProviderObjectId = providerObjectId;
        operation.ProviderStatus = providerStatus;
        operation.LeaseToken = null;
        operation.LeaseUntil = null;
        operation.NextAttemptAt = timeProvider.GetUtcNow().Add(s_pendingCheckInterval);
        operation.UpdatedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RecordRefundConfirmationRetryAsync(
        OperationLease lease,
        ProviderRefund refund,
        string errorCode,
        CancellationToken cancellationToken)
    {
        this.ClearTrackedEntities();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var operation = await this.LoadLeasedOperationAsync(lease, cancellationToken);
        if (operation is null)
        {
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        operation.Status = OperationStatus.AwaitingProvider;
        operation.ProviderObjectId = refund.Id;
        operation.ProviderStatus = refund.Status;
        operation.LastError = errorCode;
        operation.LeaseToken = null;
        operation.LeaseUntil = null;
        operation.NextAttemptAt = timeProvider.GetUtcNow().Add(this.GetRetryDelay(operation.AttemptCount));
        operation.PaymentOrder.LastError = errorCode;
        operation.UpdatedAt = timeProvider.GetUtcNow();
        this.AddAuditEntry(operation.PaymentOrder, operation, "refund_confirmation", errorCode,
            "PaymentProviderException");
        logger.LogError("Ошибка проверки возврата {PaymentOrderId} {OperationId} {CommandId} {Version} {Attempt} {ErrorCode}.",
            operation.PaymentOrder.Id, operation.Id, operation.CommandId, operation.PaymentOrder.Version,
            operation.AttemptCount, errorCode);
        await dbContext.SaveChangesAsync(cancellationToken);
        await eventPublisher.PublishOperationStateAsync(operation.PaymentOrder, operation, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RecordOperationFailureAsync(
        OperationLease lease,
        string errorCode,
        bool isDefinitive,
        bool requiresManualReview,
        CancellationToken cancellationToken,
        string? errorType = null)
    {
        this.ClearTrackedEntities();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var operation = await this.LoadLeasedOperationAsync(lease, cancellationToken);
        if (operation is null)
        {
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        var previousStatus = operation.Status;
        var previousError = operation.LastError;
        operation.Status = requiresManualReview
            ? OperationStatus.ManualReview
            : isDefinitive ? OperationStatus.Rejected : OperationStatus.OutcomeUnknown;
        operation.LastError = errorCode;
        operation.UpdatedAt = timeProvider.GetUtcNow();
        operation.LeaseUntil = null;
        operation.LeaseToken = null;
        operation.NextAttemptAt = operation.Status is OperationStatus.Rejected or OperationStatus.ManualReview
            ? DateTimeOffset.MaxValue
            : timeProvider.GetUtcNow().Add(this.GetRetryDelay(operation.AttemptCount));
        var order = operation.PaymentOrder;
        if (operation.Kind == OperationKind.CreatePayment)
        {
            order.LastError = errorCode;
            order.NextCheckAt = operation.NextAttemptAt;
        }

        this.AddAuditEntry(order, operation, "operation", errorCode, errorType);
        logger.LogError("Ошибка операции {PurchaseId} {PaymentOrderId} {OperationId} {CommandId} {Version} {Attempt} {ErrorCode} {ErrorType}.",
            order.PurchaseId, order.Id, operation.Id, operation.CommandId, order.Version, operation.AttemptCount,
            errorCode, errorType ?? "PaymentProviderException");
        await dbContext.SaveChangesAsync(cancellationToken);
        if (operation.Status != previousStatus || operation.LastError != previousError)
        {
            if (operation.Kind == OperationKind.Refund)
            {
                await eventPublisher.PublishOperationStateAsync(order, operation, cancellationToken);
            }
            else
            {
                await eventPublisher.PublishStateAsync(order, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RecordReconciliationFailureAsync(
        Guid paymentOrderId,
        string errorCode,
        string errorType,
        CancellationToken cancellationToken)
    {
        this.ClearTrackedEntities();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var order = await dbContext.PaymentOrders
            .FromSqlInterpolated($"SELECT * FROM \"payments\".\"PaymentOrders\" WHERE \"Id\" = {paymentOrderId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            await transaction.CommitAsync(cancellationToken);

            return;
        }

        order.ReconciliationFailureCount++;
        order.LastError = errorCode;
        order.NextCheckAt = timeProvider.GetUtcNow().Add(this.GetRetryDelay(order.ReconciliationFailureCount));
        this.AddAuditEntry(order, null, "reconciliation", errorCode, errorType);
        logger.LogError("Ошибка сверки {PaymentOrderId} {PurchaseId} {Version} {Attempt} {ErrorCode} {ErrorType}.",
            order.Id, order.PurchaseId, order.Version, order.ReconciliationFailureCount, errorCode, errorType);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<ProviderOperation?> LoadLeasedOperationAsync(OperationLease lease, CancellationToken cancellationToken)
    {
        var order = await dbContext.PaymentOrders
            .FromSqlInterpolated($"SELECT * FROM \"payments\".\"PaymentOrders\" WHERE \"Id\" = {lease.PaymentOrderId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            return null;
        }

        var operation = await dbContext.ProviderOperations
            .FromSqlInterpolated($"SELECT * FROM \"payments\".\"ProviderOperations\" WHERE \"Id\" = {lease.OperationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (operation is null || operation.LeaseToken != lease.LeaseToken)
        {
            return null;
        }

        operation.PaymentOrder = order;

        return operation;
    }

    private async Task MarkManualReviewAsync(ProviderOperation operation, string errorCode, CancellationToken cancellationToken)
    {
        operation.Status = OperationStatus.ManualReview;
        operation.LastError = errorCode;
        operation.UpdatedAt = timeProvider.GetUtcNow();
        operation.LeaseToken = null;
        operation.LeaseUntil = null;
        operation.NextAttemptAt = DateTimeOffset.MaxValue;
        if (operation.Kind == OperationKind.CreatePayment)
        {
            operation.PaymentOrder.LastError = errorCode;
            operation.PaymentOrder.NextCheckAt = DateTimeOffset.MaxValue;
        }

        this.AddAuditEntry(operation.PaymentOrder, operation, "operation", errorCode, "ProviderResponseMismatch");
        logger.LogError("Операция требует ручной проверки {PurchaseId} {PaymentOrderId} {OperationId} {CommandId} {Version} {Attempt} {ErrorCode}.",
            operation.PaymentOrder.PurchaseId, operation.PaymentOrder.Id, operation.Id, operation.CommandId,
            operation.PaymentOrder.Version, operation.AttemptCount, errorCode);
        if (operation.Kind == OperationKind.Refund)
        {
            await eventPublisher.PublishOperationStateAsync(operation.PaymentOrder, operation, cancellationToken);
        }
        else
        {
            await eventPublisher.PublishStateAsync(operation.PaymentOrder, cancellationToken);
        }
    }

    private bool IsValidRefund(ProviderRefund refund, ProviderOperation operation, string? providerPaymentId = null)
    {
        var paymentId = providerPaymentId ?? operation.PaymentOrder.ProviderPaymentId;

        return refund.Id.Length is > 0 and <= 128
            && refund.PaymentId == paymentId
            && refund.AmountMinor == operation.AmountMinor
            && refund.Currency == operation.PaymentOrder.Currency
            && refund.Status is "pending" or "succeeded" or "canceled";
    }

    private TimeSpan GetRetryDelay(int attemptCount)
    {
        var exponent = Math.Min(attemptCount, 8);
        var seconds = Math.Min(300, Math.Pow(2, exponent));

        return TimeSpan.FromSeconds(seconds + Random.Shared.Next(0, 4));
    }

    private async Task ScheduleWebhookRetryAsync(Guid webhookId, string errorCode, CancellationToken cancellationToken)
    {
        var notification = await dbContext.WebhookNotifications.SingleOrDefaultAsync(item => item.Id == webhookId, cancellationToken);
        if (notification is null || notification.ProcessedAt is not null)
        {
            return;
        }

        notification.AttemptCount++;
        notification.LastError = errorCode;
        notification.NextAttemptAt = timeProvider.GetUtcNow().Add(this.GetRetryDelay(notification.AttemptCount));
        this.AddAuditEntry(null, null, "webhook", errorCode, "WebhookProcessingFailure");
        logger.LogError("Ошибка повтора webhook {WebhookId} {Attempt} {ErrorCode}.",
            webhookId, notification.AttemptCount, errorCode);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkWebhookProcessedAsync(Guid webhookId, CancellationToken cancellationToken)
    {
        var notification = await dbContext.WebhookNotifications.SingleOrDefaultAsync(item => item.Id == webhookId, cancellationToken);
        if (notification is null || notification.ProcessedAt is not null)
        {
            return;
        }

        notification.ProcessedAt = timeProvider.GetUtcNow();
        notification.LastError = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessOperationSafelyAsync(Guid operationId, CancellationToken cancellationToken)
    {
        try
        {
            await this.ProcessOperationAsync(operationId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError("Ошибка обработки платёжной операции {OperationId}; тип {ErrorType}.",
                operationId, exception.GetType().Name);
        }
        finally
        {
            this.ClearTrackedEntities();
        }
    }

    private void ClearTrackedEntities() => dbContext.ChangeTracker.Clear();

    private void AddAuditEntry(
        PaymentOrder? order,
        ProviderOperation? operation,
        string stage,
        string code,
        string? errorType)
    {
        dbContext.PaymentAuditEntries.Add(new PaymentAuditEntry
        {
            Id = Guid.NewGuid(),
            PaymentOrderId = order?.Id ?? operation?.PaymentOrderId,
            ProviderOperationId = operation?.Id,
            Stage = stage,
            Code = code,
            ErrorType = errorType,
            AggregateVersion = order?.Version ?? 0,
            AmountMinor = operation?.AmountMinor ?? order?.AmountMinor ?? 0,
            RefundedAmountMinor = order?.RefundedAmountMinor ?? 0,
            OccurredAt = timeProvider.GetUtcNow()
        });
    }

    private bool IsDisabled() => string.Equals(options.Value.Mode, "Disabled", StringComparison.OrdinalIgnoreCase);

    private sealed record OperationLease(Guid OperationId, Guid PaymentOrderId, OperationKind Kind, Guid LeaseToken);
}
