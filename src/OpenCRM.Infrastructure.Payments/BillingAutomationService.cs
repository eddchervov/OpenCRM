using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Payments;

namespace OpenCRM.Infrastructure.Payments;

/// <summary> Сервис автоматизации сверки и подтверждения платежей. </summary>
public sealed class BillingAutomationService(
    OpenCRMContext context,
    IYooKassaService yooKassa,
    IPaymentConfirmationService paymentConfirmation,
    ILogger<BillingAutomationService> logger) : IBillingAutomationService
{
    /// <summary> Зависимость обработчика создания платежей. </summary>
    private readonly PaymentCreationProcessor? _paymentCreationProcessor;

    /// <summary> Создаёт сервис с обработчиком создания платежей. </summary>
    internal BillingAutomationService(
        OpenCRMContext context,
        IYooKassaService yooKassa,
        IPaymentConfirmationService paymentConfirmation,
        ILogger<BillingAutomationService> logger,
        PaymentCreationProcessor paymentCreationProcessor)
        : this(context, yooKassa, paymentConfirmation, logger)
    {
        this._paymentCreationProcessor = paymentCreationProcessor;
    }

    /// <summary> Сверяет локальные платежи со статусами платёжного провайдера. </summary>
    public async Task ReconcilePaymentsAsync()
    {
        if (this._paymentCreationProcessor != null)
        {
            await this._paymentCreationProcessor.RetryPendingOperationsAsync();
        }

        var now = DateTime.UtcNow;
        var candidates = await context.SubscriptionPayments.AsNoTracking()
            .Where(payment => payment.PaymentProviderCode != "payments-service" && payment.ExternalPaymentId != null
                && payment.CreatedAt <= now.AddMinutes(-2)
                && (payment.LastReconciledAt == null || payment.LastReconciledAt <= now.AddMinutes(-15))
                && (payment.PaymentStatus == "pending" || payment.PaymentStatus == "waiting_for_capture"
                    || payment.PaymentStatus == "paid_waiting_confirmation"
                    || payment.PaymentStatus == "completed" || payment.PaymentStatus == "refunded"
                    || payment.PaymentStatus == "failed" && payment.PaidAt != null
                    || payment.PaymentStatus == "canceled" && payment.PaidAt != null))
            .OrderBy(payment => payment.LastReconciledAt != null)
            .ThenBy(payment => payment.LastReconciledAt)
            .ThenBy(payment => payment.Id)
            .Select(payment => new { payment.Id, payment.ExternalPaymentId, payment.Amount, payment.PaymentStatus })
            .Take(100)
            .ToListAsync();

        foreach (var candidate in candidates)
        {
            try
            {
                await this.ReconcilePaymentAsync(candidate.Id, candidate.ExternalPaymentId!, candidate.Amount, candidate.PaymentStatus);
            }
            catch (Exception exception)
            {
                context.ChangeTracker.Clear();
                logger.LogError(exception, "Не удалось сверить платеж {PaymentId} с YooKassa.", candidate.Id);
            }
        }
    }

    /// <summary> Сверяет один платёж с внешним статусом. </summary>
    private async Task ReconcilePaymentAsync(int paymentId, string externalId, decimal amount, string localStatus)
    {
        var remote = await yooKassa.GetPaymentStatusAsync(externalId);
        if (!remote.IsSuccess || remote.PaymentId != externalId || remote.Currency != "RUB"
            || remote.Amount != amount || remote.RefundedAmount < 0 || remote.RefundedAmount > amount
            || remote.Status == "canceled" && remote.Paid
            || remote.RefundedAmount > 0 && remote.RefundedCurrency != "RUB")
        {
            logger.LogWarning("Сверка платежа {PaymentId} отклонена: данные YooKassa не совпадают с локальными.", paymentId);
            return;
        }

        if (remote.Status == "succeeded" && remote.Paid && remote.RefundedAmount < amount
            && localStatus is ("pending" or "waiting_for_capture" or "paid_waiting_confirmation"))
        {
            if (!await paymentConfirmation.ConfirmAsync(externalId))
            {
                logger.LogError("Оплаченный платеж {PaymentId} требует ручного разбора ошибки активации.", paymentId);
            }
        }
        context.ChangeTracker.Clear();
        var payment = await context.SubscriptionPayments.FirstAsync(item => item.Id == paymentId);
        if (remote.Status == "canceled" && !remote.Paid)
        {
            payment.PaidAt = null;
            payment.RefundedAmount = 0;
            if (payment.PaymentStatus != "canceled")
            {
                context.AdminPaymentStatusAudits.Add(new AdminPaymentStatusAudit
                {
                    PaymentId = paymentId,
                    PreviousStatus = payment.PaymentStatus,
                    NewStatus = "canceled",
                    Source = "yookassa_reconciliation",
                    ChangedByUserId = "system:yookassa",
                    ChangedAt = DateTime.UtcNow
                });
                payment.PaymentStatus = "canceled";
                payment.StatusUpdatedAt = DateTime.UtcNow;
            }
        }

        if (remote.Status == "succeeded" && remote.Paid && remote.RefundedAmount == amount
            && payment.PaymentStatus is ("pending" or "waiting_for_capture" or "paid_waiting_confirmation"))
        {
            payment.PaidAt ??= DateTime.UtcNow;
            payment.PaymentStatus = "refunded";
            payment.StatusUpdatedAt = DateTime.UtcNow;
            context.AdminPaymentStatusAudits.Add(new AdminPaymentStatusAudit
            {
                PaymentId = paymentId,
                PreviousStatus = localStatus,
                NewStatus = "refunded",
                Source = "yookassa_reconciliation",
                ChangedByUserId = "system:yookassa",
                ChangedAt = DateTime.UtcNow
            });
        }

        if (remote.Status == "succeeded" && remote.Paid
            && payment.PaymentStatus is ("completed" or "refunded" or "failed"))
        {
            if (payment.RefundedAmount != remote.RefundedAmount)
            {
                payment.RefundedAmount = remote.RefundedAmount;
            }

            var isFullyRefunded = remote.RefundedAmount == amount;
            var newStatus = isFullyRefunded ? "refunded" : payment.PaymentStatus == "failed" ? "failed" : "completed";
            if (payment.PaymentStatus != newStatus)
            {
                context.AdminPaymentStatusAudits.Add(new AdminPaymentStatusAudit
                {
                    PaymentId = paymentId,
                    PreviousStatus = payment.PaymentStatus,
                    NewStatus = newStatus,
                    Source = "yookassa_reconciliation",
                    ChangedByUserId = "system:yookassa",
                    ChangedAt = DateTime.UtcNow
                });
                payment.PaymentStatus = newStatus;
                payment.StatusUpdatedAt = DateTime.UtcNow;
            }
        }

        payment.LastReconciledAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }
}
