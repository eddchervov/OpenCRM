using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Billing.Contracts.Dtos;
using Helpers.Payments;
using OpenCRM.Services.Interfaces.Payments;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис административных команд по платежам. </summary>
internal sealed class AdminPaymentCommandService(
    OpenCRMContext opencrmContext,
    IYooKassaService yooKassaService,
    IPaymentConfirmationService? paymentConfirmationService = null) : IAdminPaymentCommandService
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Зависимость сервиса YooKassa. </summary>
    private readonly IYooKassaService _yooKassaService = yooKassaService;

    /// <summary> Зависимость сервиса подтверждения платежей. </summary>
    private readonly IPaymentConfirmationService? _paymentConfirmationService = paymentConfirmationService;

    /// <summary> Синхронизирует статус платежа с YooKassa. </summary>
    public async Task<AdminBillingPaymentOperationDto> SyncPaymentWithYooKassaAsync(
        int paymentId,
        string changedByUserId)
    {
        var payment = await this._opencrmContext.SubscriptionPayments.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == paymentId);

        if (payment == null)
        {
            return AdminBillingOperationResults.PaymentMissing("Платеж не найден.");
        }

        if (payment.PaymentProviderCode == "payments-service")
        {
            return AdminBillingOperationResults.PaymentInvalid("Оплата управляется платёжным сервисом. Сверка выполняется автоматически; ручная подмена денежного состояния недоступна.");
        }

        if (string.IsNullOrWhiteSpace(payment.ExternalPaymentId))
        {
            return AdminBillingOperationResults.PaymentInvalid("У платежа нет external id YooKassa.");
        }

        var remoteStatus = await this._yooKassaService.GetPaymentStatusAsync(payment.ExternalPaymentId);
        if (!remoteStatus.IsSuccess)
        {
            return AdminBillingOperationResults.PaymentInvalid(remoteStatus.ErrorMessage);
        }

        if (remoteStatus.PaymentId != payment.ExternalPaymentId || remoteStatus.Currency != "RUB"
            || remoteStatus.Amount != payment.Amount || remoteStatus.RefundedAmount < 0
            || remoteStatus.RefundedAmount > payment.Amount
            || remoteStatus.Status == "canceled" && remoteStatus.Paid
            || remoteStatus.RefundedAmount > 0 && remoteStatus.RefundedCurrency != "RUB")
        {
            return AdminBillingOperationResults.PaymentInvalid("Данные платежа в YooKassa не совпадают с локальными.");
        }

        if (remoteStatus.Status == "succeeded" && remoteStatus.Paid
            && remoteStatus.RefundedAmount < payment.Amount
            && payment.PaymentStatus is ("pending" or "waiting_for_capture" or "paid_waiting_confirmation"))
        {
            if (this._paymentConfirmationService == null)
            {
                return AdminBillingOperationResults.PaymentInvalid("Сервис активации подписки недоступен.");
            }

            await this._paymentConfirmationService.ConfirmAsync(payment.ExternalPaymentId);
        }

        this._opencrmContext.ChangeTracker.Clear();
        payment = await this._opencrmContext.SubscriptionPayments.FirstAsync(item => item.Id == paymentId);

        if (payment.PaymentStatus is ("completed" or "refunded")
            && remoteStatus.Status is not ("succeeded" or "canceled"))
        {
            return AdminBillingOperationResults.PaymentInvalid("YooKassa вернула состояние, противоречащее уже подтвержденной оплате. Проверьте платеж вручную.");
        }

        var isRemoteSucceeded = remoteStatus.Status == "succeeded" && remoteStatus.Paid;
        var localStatus = isRemoteSucceeded
            ? remoteStatus.RefundedAmount == payment.Amount ? "refunded" : payment.PaymentStatus
            : AdminPaymentCommandService.ResolveLocalStatusFromYooKassa(remoteStatus.Status, remoteStatus.Paid);
        if (isRemoteSucceeded && localStatus is ("pending" or "waiting_for_capture" or "paid_waiting_confirmation"))
        {
            return AdminBillingOperationResults.PaymentInvalid("Оплата подтверждена, но подписка еще не активирована. Проверьте журнал и повторите сверку позже.");
        }

        var previousStatus = payment.PaymentStatus;
        var remoteCreatedAt = remoteStatus.CreatedAt;
        var hasRemoteCreatedAt = remoteCreatedAt.HasValue && remoteCreatedAt.Value != default;
        var isCreationDateChanged = hasRemoteCreatedAt && payment.CreatedAt != remoteCreatedAt.GetValueOrDefault();
        if (isCreationDateChanged)
        {
            payment.CreatedAt = remoteCreatedAt!.Value;
        }

        var isPaidAtMissing = remoteStatus.Status == "succeeded" && remoteStatus.Paid && payment.PaidAt == null;
        if (isPaidAtMissing)
        {
            payment.PaidAt = DateTime.UtcNow;
        }

        var isPaidAtCleared = remoteStatus.Status == "canceled" && !remoteStatus.Paid && payment.PaidAt != null;
        if (isPaidAtCleared)
        {
            payment.PaidAt = null;
        }

        var isRefundedAmountChanged = payment.RefundedAmount != remoteStatus.RefundedAmount;
        payment.RefundedAmount = remoteStatus.RefundedAmount;
        payment.LastReconciledAt = DateTime.UtcNow;

        if (string.Equals(previousStatus, localStatus, StringComparison.OrdinalIgnoreCase))
        {
            await this._opencrmContext.SaveChangesAsync();

            return new AdminBillingPaymentOperationDto
            {
                Succeeded = true,
                Message = isPaidAtCleared
                    ? "YooKassa подтвердила отмену. Ошибочная дата оплаты удалена; проверьте доступ по подписке."
                    : isCreationDateChanged || isPaidAtMissing || isRefundedAmountChanged
                        ? "Статус не изменился, данные оплаты сверены с YooKassa."
                        : $"Статус не изменился: {AdminPaymentDisplay.ResolvePaymentStatusName(localStatus)}.",
                Status = payment.PaymentStatus,
                StatusName = AdminPaymentDisplay.ResolvePaymentStatusName(payment.PaymentStatus),
                StatusGroup = AdminPaymentDisplay.ResolvePaymentStatusGroup(payment.PaymentStatus),
                RemoteStatus = remoteStatus.Status,
                Paid = remoteStatus.Paid,
                Amount = remoteStatus.Amount
            };
        }

        var changedAt = DateTime.UtcNow;
        payment.PaymentStatus = localStatus;
        payment.StatusUpdatedAt = changedAt;
        this.AddPaymentStatusAudit(
            payment,
            previousStatus,
            localStatus,
            "yookassa_sync",
            null,
            changedByUserId,
            changedAt);
        await this._opencrmContext.SaveChangesAsync();

        return new AdminBillingPaymentOperationDto
        {
            Succeeded = true,
            Message = localStatus == "canceled" && previousStatus is ("completed" or "refunded")
                ? "YooKassa подтвердила отмену. Покупка удалена из списка; проверьте доступ по подписке."
                : $"Статус обновлен: {AdminPaymentDisplay.ResolvePaymentStatusName(localStatus)}.",
            Status = payment.PaymentStatus,
            StatusName = AdminPaymentDisplay.ResolvePaymentStatusName(payment.PaymentStatus),
            StatusGroup = AdminPaymentDisplay.ResolvePaymentStatusGroup(payment.PaymentStatus),
            RemoteStatus = remoteStatus.Status,
            Paid = remoteStatus.Paid,
            Amount = remoteStatus.Amount
        };
    }

    /// <summary> Устанавливает статус платежа вручную. </summary>
    public async Task<AdminBillingPaymentOperationDto> MarkPaymentStatusAsync(
        int paymentId,
        string? status,
        string? expectedCurrentStatus,
        string? reason,
        string changedByUserId)
    {
        var normalizedStatus = status?.Trim().ToLowerInvariant();
        if (normalizedStatus is not ("failed" or "refunded" or "canceled" or "pending" or "completed"))
        {
            return AdminBillingOperationResults.PaymentInvalid("Недопустимый статус платежа.");
        }

        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason))
        {
            return AdminBillingOperationResults.PaymentInvalid("Укажите причину ручной корректировки.");
        }

        if (normalizedReason.Length > 500)
        {
            return AdminBillingOperationResults.PaymentInvalid("Причина ручной корректировки не должна превышать 500 символов.");
        }

        var payment = await this._opencrmContext.SubscriptionPayments
            .FirstOrDefaultAsync(item => item.Id == paymentId);

        if (payment == null)
        {
            return AdminBillingOperationResults.PaymentMissing("Платеж не найден.");
        }

        if (payment.PaymentProviderCode == "payments-service")
        {
            return AdminBillingOperationResults.PaymentInvalid("Денежное состояние этой оплаты подтверждает платёжный сервис. Используйте финансовый журнал для разбора расхождений.");
        }

        var currentStatus = payment.PaymentStatus.Trim().ToLowerInvariant();
        var normalizedExpectedStatus = expectedCurrentStatus?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!string.Equals(currentStatus, normalizedExpectedStatus, StringComparison.Ordinal))
        {
            return AdminBillingOperationResults.PaymentInvalid("Статус платежа уже изменился. Обновите список и повторите операцию.");
        }

        if (string.Equals(currentStatus, normalizedStatus, StringComparison.Ordinal))
        {
            return new AdminBillingPaymentOperationDto
            {
                Succeeded = true,
                Message = "У платежа уже установлен выбранный статус.",
                Status = payment.PaymentStatus,
                StatusName = AdminPaymentDisplay.ResolvePaymentStatusName(payment.PaymentStatus),
                StatusGroup = AdminPaymentDisplay.ResolvePaymentStatusGroup(payment.PaymentStatus)
            };
        }

        var changedAt = DateTime.UtcNow;
        payment.PaymentStatus = normalizedStatus;
        payment.StatusUpdatedAt = changedAt;
        this.AddPaymentStatusAudit(
            payment,
            currentStatus,
            normalizedStatus,
            "manual",
            normalizedReason,
            changedByUserId,
            changedAt);
        await this._opencrmContext.SaveChangesAsync();

        return new AdminBillingPaymentOperationDto
        {
            Succeeded = true,
            Message = $"Платеж помечен: {AdminPaymentDisplay.ResolvePaymentStatusName(normalizedStatus)}.",
            Status = payment.PaymentStatus,
            StatusName = AdminPaymentDisplay.ResolvePaymentStatusName(payment.PaymentStatus),
            StatusGroup = AdminPaymentDisplay.ResolvePaymentStatusGroup(payment.PaymentStatus)
        };
    }

    /// <summary> Добавляет запись аудита изменения статуса платежа. </summary>
    private void AddPaymentStatusAudit(
        SubscriptionPayment payment,
        string previousStatus,
        string newStatus,
        string source,
        string? reason,
        string changedByUserId,
        DateTime changedAt)
    {
        this._opencrmContext.AdminPaymentStatusAudits.Add(new AdminPaymentStatusAudit
        {
            Payment = payment,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            Source = source,
            Reason = reason,
            ChangedByUserId = changedByUserId,
            ChangedAt = changedAt
        });
    }

    /// <summary> Определяет локальный статус по статусу YooKassa. </summary>
    private static string ResolveLocalStatusFromYooKassa(string? remoteStatus, bool paid)
    {
        var normalized = (remoteStatus ?? string.Empty).Trim().ToLowerInvariant();

        if (paid || normalized == "succeeded")
        {
            return "paid_waiting_confirmation";
        }

        return normalized switch
        {
            "canceled" => "canceled",
            "waiting_for_capture" => "waiting_for_capture",
            "pending" => "pending",
            "" => "pending",
            var value => value
        };
    }
}
