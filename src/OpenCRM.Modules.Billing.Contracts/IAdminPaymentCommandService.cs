using OpenCRM.Modules.Billing.Contracts.Dtos;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>Сверяет оплаты с провайдером и сохраняет административные корректировки с аудитом.</summary>
public interface IAdminPaymentCommandService
{
    /// <summary> Сверяет статус платежа с YooKassa. </summary>
    Task<AdminBillingPaymentOperationDto> SyncPaymentWithYooKassaAsync(
        int paymentId,
        string changedByUserId);

    /// <summary> Изменяет статус платежа с указанием причины. </summary>
    Task<AdminBillingPaymentOperationDto> MarkPaymentStatusAsync(
        int paymentId,
        string? status,
        string? expectedCurrentStatus,
        string? reason,
        string changedByUserId);
}
