namespace OpenCRM.WebApp.Models.Payments;

/// <summary> Модель страницы результата завершения оплаты. </summary>
public class PaymentCompleteViewModel
{
    /// <summary> Признак подтверждённой оплаты. </summary>
    public bool IsConfirmed { get; set; }

    /// <summary> Признак отменённой оплаты. </summary>
    public bool IsCanceled { get; set; }

    /// <summary> Идентификатор покупки, статус которой проверяется на странице возврата. </summary>
    public Guid? PurchaseId { get; set; }

    /// <summary> Адрес проверки статуса именно этой покупки. </summary>
    public string? PurchaseStatusUrl { get; set; }

    /// <summary> Признак необходимости ограниченной проверки статуса покупки. </summary>
    public bool ShouldPollPurchase { get; set; }

    /// <summary> Заголовок страницы результата. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Сообщение о результате оплаты. </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary> CSS-класс иконки результата. </summary>
    public string IconCssClass { get; set; } = "fa-clock-o text-primary";

    /// <summary> Адрес основного действия на странице результата. </summary>
    public string PrimaryActionUrl { get; set; } = "/subscription";

    /// <summary> Текст основного действия. </summary>
    public string PrimaryActionText { get; set; } = "К тарифам";
}

