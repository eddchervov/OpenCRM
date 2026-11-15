namespace OpenCRM.Services.Interfaces.Payments.Dto;

/// <summary> Результат операции с платежом. </summary>
public class PaymentResponse
{
    /// <summary> Признак успешного результата. </summary>
    public bool IsSuccess { get; set; }

    /// <summary> Сообщение об ошибке операции. </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary> Ссылка на оплату, если есть. </summary>
    public string? PaymentLink { get; set; }

    /// <summary> Идентификатор платежа, если есть. </summary>
    public string? PaymentId { get; set; }

    /// <summary> Статус платежа, если известен. </summary>
    public string? Status { get; set; }

    /// <summary> Сопроводительное сообщение результата. </summary>
    public string? Message { get; set; }

    /// <summary> Признак оплаты платежа. </summary>
    public bool Paid { get; set; }

    /// <summary> Сумма платежа. </summary>
    public decimal Amount { get; set; }

    /// <summary> Валюта платежа. </summary>
    public string? Currency { get; set; }

    /// <summary> Сумма возврата по платежу. </summary>
    public decimal RefundedAmount { get; set; }

    /// <summary> Валюта возврата. </summary>
    public string? RefundedCurrency { get; set; }

    /// <summary> Дата создания платежа. </summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary> Создаёт успешный результат операции платежа. </summary>
    public static PaymentResponse CreateSuccess(string? paymentLink = null, string? paymentId = null, string? message = null)
    {
        return new PaymentResponse
        {
            IsSuccess = true,
            PaymentLink = paymentLink,
            PaymentId = paymentId,
            Message = message
        };
    }

    /// <summary> Создаёт результат операции с ошибкой. </summary>
    public static PaymentResponse CreateError(string message)
    {
        return new PaymentResponse
        {
            IsSuccess = false,
            ErrorMessage = message
        };
    }
}
