namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Покупка в административном отчёте биллинга. </summary>
public sealed class AdminBillingPurchaseDto
{
    /// <summary> Идентификатор платежа. </summary>
    public int PaymentId { get; init; }

    /// <summary> Идентификатор пользователя. </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary> Отображаемое имя пользователя. </summary>
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

    /// <summary> Возвращённая сумма по платежу. </summary>
    public decimal RefundedAmount { get; init; }

    /// <summary> Дата и время оплаты. </summary>
    public DateTime PaidAt { get; init; }

    /// <summary> Признак выдачи чека самозанятого. </summary>
    public bool NpdReceiptIssued { get; init; }

    /// <summary> Дата и время отметки выдачи чека самозанятого. </summary>
    public DateTime? NpdReceiptIssuedAt { get; init; }

    /// <summary> Пользователь, отметивший выдачу чека самозанятого. </summary>
    public string? NpdReceiptMarkedBy { get; init; }

    /// <summary> Причина отметки чека самозанятого. </summary>
    public string? NpdReceiptReason { get; init; }

    /// <summary> Ссылка на чек самозанятого. </summary>
    public string? NpdReceiptUrl { get; init; }

    /// <summary> Имя файла чека самозанятого. </summary>
    public string? NpdReceiptFileName { get; init; }

    /// <summary> Ссылка на файл чека самозанятого. </summary>
    public string? NpdReceiptFileUrl { get; init; }

    /// <summary> Комментарий к чеку самозанятого. </summary>
    public string? NpdReceiptComment { get; init; }

    /// <summary> Статус доставки чека самозанятого. </summary>
    public string NpdReceiptDeliveryStatus { get; set; } = "not_sent";

    /// <summary> Дата и время отправки чека самозанятого. </summary>
    public DateTime? NpdReceiptSentAt { get; set; }

    /// <summary> Адресат отправки чека самозанятого. </summary>
    public string? NpdReceiptSentTo { get; set; }

    /// <summary> Ошибка отправки чека самозанятого. </summary>
    public string? NpdReceiptSendError { get; set; }
}
