using OpenCRM.Services.Interfaces.Payments.Dto;
using Microsoft.Extensions.Logging;
using OpenCRM.Services.Interfaces.Payments;

namespace OpenCRM.Infrastructure.Payments;

/// <summary> Сервис обработки вебхуков YooKassa. </summary>
public sealed class YooKassaWebhookService(
    IPaymentConfirmationService paymentConfirmationService,
    IYooKassaService yooKassaService,
    ILogger<YooKassaWebhookService> logger) : IYooKassaWebhookService
{
    /// <summary> Обрабатывает уведомление от YooKassa. </summary>
    public async Task HandleAsync(YooKassaNotification notification)
    {
        switch (notification.Event)
        {
            case "payment.waiting_for_capture":
                await this.CapturePaymentAsync(notification.Object.Id);
                break;
            case "payment.succeeded":
                if (!await paymentConfirmationService.ConfirmAsync(notification.Object.Id))
                {
                    logger.LogError("Оплаченный платеж {PaymentId} требует ручного разбора ошибки активации.", notification.Object.Id);
                }
                break;
            case "payment.canceled":
                logger.LogInformation("Платеж отменён: {PaymentId}.", notification.Object.Id);
                await yooKassaService.UpdateLocalPaymentStatusAsync(notification.Object.Id, "canceled");
                break;
            default:
                logger.LogInformation("Необрабатываемое событие YooKassa: {Event}.", notification.Event);
                break;
        }
    }

    /// <summary> Подтверждает списание платежа по уведомлению. </summary>
    private async Task CapturePaymentAsync(string paymentId)
    {
        await yooKassaService.UpdateLocalPaymentStatusAsync(paymentId, "waiting_for_capture");
        var captureResult = await yooKassaService.CapturePaymentAsync(paymentId);
        if (captureResult.IsSuccess)
        {
            logger.LogInformation("Платеж подтверждён: {PaymentId}.", paymentId);

            return;
        }

        logger.LogError("Не удалось подтвердить платеж: {PaymentId}.", paymentId);
        await yooKassaService.UpdateLocalPaymentStatusAsync(paymentId, "failed");
    }
}
