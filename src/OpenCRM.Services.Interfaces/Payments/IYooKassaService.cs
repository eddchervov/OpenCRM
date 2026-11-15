using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Services.Interfaces.Payments;

/// <summary> Контракт сервиса платежей ЮKassa. </summary>
public interface IYooKassaService
{
    /// <summary> Создаёт платёж в ЮKassa для пользователя и тарифа. </summary>
    Task<PaymentResponse> CreatePaymentAsync(
        string userId,
        int planId,
        decimal amount,
        string description,
        string operationType,
        PaymentBuyerDto buyer,
        string? returnUrl = null,
        PaymentPromotionDto? promotion = null,
        string? idempotenceKey = null,
        DateTime? requestTimestamp = null);

    /// <summary> Подтверждает списание по платежу в ЮKassa. </summary>
    Task<PaymentResponse> CapturePaymentAsync(string paymentId);

    /// <summary> Отменяет платёж в ЮKassa. </summary>
    Task<PaymentResponse> CancelPaymentAsync(string paymentId);

    /// <summary> Возвращает статус платежа из ЮKassa. </summary>
    Task<PaymentResponse> GetPaymentStatusAsync(string paymentId);

    /// <summary> Отмечает локальный платёж как оплаченный. </summary>
    Task<bool> MarkPaymentPaidAsync(string paymentId);

    /// <summary> Возвращает локальные данные платежа по его идентификатору. </summary>
    Task<LocalPaymentInfoDto?> GetLocalPaymentInfoAsync(string paymentId);

    /// <summary> Обновляет локальный статус платежа. </summary>
    Task<bool> UpdateLocalPaymentStatusAsync(string paymentId, string status);

    /// <summary> Проверяет подпись уведомления вебхука. </summary>
    Task<bool> VerifyNotification(string ipnData, string signature);
}

