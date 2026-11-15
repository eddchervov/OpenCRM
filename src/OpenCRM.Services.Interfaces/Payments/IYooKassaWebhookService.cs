using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Services.Interfaces.Payments;

/// <summary> Контракт обработки вебхуков ЮKassa. </summary>
public interface IYooKassaWebhookService
{
    /// <summary> Обрабатывает уведомление вебхука ЮKassa. </summary>
    Task HandleAsync(YooKassaNotification notification);
}
