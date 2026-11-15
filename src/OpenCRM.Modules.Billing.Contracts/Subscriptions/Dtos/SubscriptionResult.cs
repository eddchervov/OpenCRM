namespace OpenCRM.Services.Interfaces.Subscriptions.Dtos;

/// <summary> Результат операции с подпиской. </summary>
public class SubscriptionResult
{
    /// <summary> Признак успешной операции. </summary>
    public bool Success { get; set; }

    /// <summary> Текст ошибки операции. </summary>
    public string? Error { get; set; }

    /// <summary> Ссылка на оплату. </summary>
    public string? PaymentUrl { get; set; }

    /// <summary> Идентификатор платежа. </summary>
    public string? PaymentId { get; set; }

    /// <summary> Признак необходимости оплаты. </summary>
    public bool RequiresPayment { get; set; }

    /// <summary> Создаёт результат успешной операции. </summary>
    public static SubscriptionResult CreateSuccess() => new() { Success = true };

    /// <summary> Создаёт результат неудачной операции с ошибкой. </summary>
    public static SubscriptionResult CreateFailed(string error) => new() { Success = false, Error = error };

    /// <summary> Создаёт результат, требующий перехода к оплате. </summary>
    public static SubscriptionResult CreatePaymentRequired(string paymentUrl, string paymentId)
    {
        return new SubscriptionResult
        {
            Success = true,
            RequiresPayment = true,
            PaymentUrl = paymentUrl,
            PaymentId = paymentId
        };
    }
}
