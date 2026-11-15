using OpenCRM.Modules.Billing.Contracts.Dtos;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>Управляет отметкой, реквизитами и файлами чеков подтверждённых оплат.</summary>
public interface IAdminNpdReceiptService
{
    /// <summary> Отмечает факт выдачи чека самозанятого по платежу. </summary>
    Task<AdminBillingPaymentOperationDto> MarkNpdReceiptAsync(
        int paymentId,
        bool issued,
        bool expectedIssued,
        string? reason,
        string adminUserId);

    /// <summary> Обновляет реквизиты чека самозанятого по платежу. </summary>
    Task<AdminBillingPaymentOperationDto> UpdateNpdReceiptDetailsAsync(
        int paymentId,
        string? receiptUrl,
        string? comment);

    /// <summary> Загружает файл чека самозанятого для платежа. </summary>
    Task<AdminBillingPaymentOperationDto> UploadNpdReceiptFileAsync(
        int paymentId,
        Stream content,
        string fileName,
        string contentType);

    /// <summary> Возвращает файл чека самозанятого по платежу. </summary>
    Task<AdminNpdReceiptFileDto?> GetNpdReceiptFileAsync(int paymentId);

    /// <summary> Удаляет файл чека самозанятого по платежу. </summary>
    Task<AdminBillingPaymentOperationDto> DeleteNpdReceiptFileAsync(int paymentId);
}
