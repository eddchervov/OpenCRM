using OpenCRM.Modules.Billing.Contracts.Dtos;

namespace OpenCRM.Services.Interfaces.Payments;

/// <summary> Контракт сервиса доставки чеков в НПД. </summary>
public interface INpdReceiptDeliveryService
{
    /// <summary> Ставит чек платежа в очередь доставки. </summary>
    Task<AdminBillingPaymentOperationDto> QueueAsync(int paymentId);

    /// <summary> Возвращает файл чека по токену. </summary>
    Task<AdminNpdReceiptFileDto?> GetFileAsync(string token);
}
