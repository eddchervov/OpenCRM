namespace OpenCRM.Admin.Api.Models;

/// <summary> Покупка для очереди чеков НПД. </summary>
public sealed class AdminBillingPurchase
{
    /// <summary> Идентификатор платежа. </summary>
    public int PaymentId { get; init; }

    /// <summary> Идентификатор пользователя. </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary> Имя пользователя. </summary>
    public string User { get; init; } = string.Empty;

    /// <summary> Email пользователя. </summary>
    public string? UserEmail { get; init; }

    /// <summary> Телефон пользователя. </summary>
    public string? UserPhone { get; init; }

    /// <summary> Тип покупателя. </summary>
    public string BuyerType { get; init; } = "unknown";

    /// <summary> Имя покупателя. </summary>
    public string? BuyerName { get; init; }

    /// <summary> ИНН покупателя. </summary>
    public string? BuyerInn { get; init; }

    /// <summary> Название тарифного плана. </summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary> Статус платежа. </summary>
    public string PaymentStatus { get; init; } = string.Empty;

    /// <summary> Сумма платежа. </summary>
    public decimal Amount { get; init; }

    /// <summary> Возвращённая сумма платежа. </summary>
    public decimal RefundedAmount { get; init; }

    /// <summary> Дата и время оплаты. </summary>
    public DateTime PaidAt { get; init; }

    /// <summary> Признак выдачи чека НПД. </summary>
    public bool NpdReceiptIssued { get; init; }

    /// <summary> Дата и время отметки выдачи чека НПД. </summary>
    public DateTime? NpdReceiptIssuedAt { get; init; }

    /// <summary> Пользователь, отметивший выдачу чека НПД. </summary>
    public string? NpdReceiptMarkedBy { get; init; }

    /// <summary> Причина отметки чека НПД. </summary>
    public string? NpdReceiptReason { get; init; }

    /// <summary> Ссылка на чек НПД. </summary>
    public string? NpdReceiptUrl { get; init; }

    /// <summary> Имя файла чека НПД. </summary>
    public string? NpdReceiptFileName { get; init; }

    /// <summary> Ссылка на файл чека НПД. </summary>
    public string? NpdReceiptFileUrl { get; init; }

    /// <summary> Комментарий к чеку НПД. </summary>
    public string? NpdReceiptComment { get; init; }

    /// <summary> Статус доставки чека НПД. </summary>
    public string NpdReceiptDeliveryStatus { get; init; } = "not_sent";

    /// <summary> Дата и время отправки чека НПД. </summary>
    public DateTime? NpdReceiptSentAt { get; init; }

    /// <summary> Адресат отправки чека НПД. </summary>
    public string? NpdReceiptSentTo { get; init; }

    /// <summary> Ошибка отправки чека НПД. </summary>
    public string? NpdReceiptSendError { get; init; }
}
