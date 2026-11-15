using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Письмо с данными обработки, доставки и статистикой. </summary>
public class EmailMessage
{
    /// <summary> Идентификатор письма. </summary>
    public int Id { get; set; }

    /// <summary> Идентификатор пользователя-получателя. </summary>
    public string? UserId { get; set; }

    /// <summary> Пользователь-получатель письма. </summary>
    public ApplicationUser? User { get; set; }

    /// <summary> Адрес электронной почты получателя. </summary>
    public string ToEmail { get; set; } = string.Empty;

    /// <summary> Тема письма. </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary> Код статуса письма. </summary>
    public string StatusCode { get; set; } = EmailMessageStatusCodes.Pending;

    /// <summary> Ключ уведомления, связанного с письмом. </summary>
    public string? NotificationKey { get; set; }

    /// <summary> Статус связанного уведомления. </summary>
    public string? NotificationStatus { get; set; }

    /// <summary> Идентификатор уведомления (GUID). </summary>
    public Guid? NotificationId { get; set; }

    /// <summary> Идентификатор команды уведомления (GUID). </summary>
    public Guid? NotificationCommandId { get; set; }

    /// <summary> Ключ шаблона письма. </summary>
    public string? TemplateKey { get; set; }

    /// <summary> JSON-данные для шаблона письма. </summary>
    public string? TemplateDataJson { get; set; }

    /// <summary> Дата и время отправки уведомления. </summary>
    public DateTime? NotificationDispatchedAt { get; set; }

    /// <summary> Дата и время следующей обработки уведомления. </summary>
    public DateTime? NextDispatchAt { get; set; }

    /// <summary> Дата и время события уведомления. </summary>
    public DateTime? NotificationEventAt { get; set; }

    /// <summary> Количество попыток отправки уведомления. </summary>
    public int NotificationAttemptCount { get; set; }

    /// <summary> Идентификатор получателя рассылки. </summary>
    public Guid? CampaignRecipientId { get; set; }

    /// <summary> Идентификатор напоминания о подписке. </summary>
    public long? SubscriptionReminderId { get; set; }

    /// <summary> Наименование провайдера отправки письма. </summary>
    public string? Provider { get; set; }

    /// <summary> Идентификатор письма у провайдера. </summary>
    public string? ProviderMessageId { get; set; }

    /// <summary> Ответ провайдера на отправку письма. </summary>
    public string? ProviderResponse { get; set; }

    /// <summary> Код последнего события по письму. </summary>
    public string? LastEventCode { get; set; }

    /// <summary> Дата и время последнего события по письму. </summary>
    public DateTime? LastEventAt { get; set; }

    /// <summary> Количество открытий письма. </summary>
    public int OpenCount { get; set; }

    /// <summary> Количество переходов по ссылкам письма. </summary>
    public int ClickCount { get; set; }

    /// <summary> HTML-тело письма. </summary>
    public string HtmlBody { get; set; } = string.Empty;

    /// <summary> Текстовое тело письма. </summary>
    public string TextBody { get; set; } = string.Empty;

    /// <summary> Сообщение об ошибке отправки письма. </summary>
    public string? ErrorMessage { get; set; }

    /// <summary> Дата и время создания письма. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время отправки письма. </summary>
    public DateTime? SentAt { get; set; }

    /// <summary> Список событий, связанных с письмом. </summary>
    public ICollection<EmailMessageEvent> Events { get; set; } = new HashSet<EmailMessageEvent>();
}

/// <summary> Событие провайдера, связанное с письмом. </summary>
public class EmailMessageEvent
{
    /// <summary> Идентификатор события уведомления (GUID). </summary>
    public Guid? NotificationEventId { get; set; }

    /// <summary> Идентификатор события. </summary>
    public long Id { get; set; }

    /// <summary> Идентификатор письма, к которому относится событие. </summary>
    public int? EmailMessageId { get; set; }

    /// <summary> Письмо, к которому относится событие. </summary>
    public EmailMessage? EmailMessage { get; set; }

    /// <summary> Наименование провайдера события. </summary>
    public string Provider { get; set; } = EmailProviders.NotificationService;

    /// <summary> Название события провайдера. </summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary> Статус, связанный с событием. </summary>
    public string? Status { get; set; }

    /// <summary> Адрес электронной почты события. </summary>
    public string? Email { get; set; }

    /// <summary> Идентификатор письма у провайдера. </summary>
    public string? ProviderMessageId { get; set; }

    /// <summary> Идентификатор рассылки, связанной с событием. </summary>
    public string? CampaignId { get; set; }

    /// <summary> URL перехода по ссылке письма. </summary>
    public string? Url { get; set; }

    /// <summary> IP-адрес, с которого произошло событие. </summary>
    public string? IpAddress { get; set; }

    /// <summary> User-Agent клиента события. </summary>
    public string? UserAgent { get; set; }

    /// <summary> Информация о доставке письма. </summary>
    public string? DeliveryInfo { get; set; }

    /// <summary> Исходные данные события. </summary>
    public string RawPayload { get; set; } = string.Empty;

    /// <summary> Признак успешной проверки подписи события. </summary>
    public bool SignatureVerified { get; set; }

    /// <summary> Дата и время события. </summary>
    public DateTime EventAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время получения события. </summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}

/// <summary> Справочник кодов статусов письма. </summary>
public static class EmailMessageStatusCodes
{
    /// <summary> Код статуса ожидания отправки письма. </summary>
    public const string Pending = "pending";

    /// <summary> Код статуса письма в очереди. </summary>
    public const string Queued = "queued";

    /// <summary> Код статуса повторной отправки письма. </summary>
    public const string Retrying = "retrying";
    public const string Simulated = "simulated";
    public const string Accepted = "accepted";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Opened = "opened";
    public const string Clicked = "clicked";
    public const string SoftBounced = "soft_bounced";
    public const string HardBounced = "hard_bounced";
    public const string Unsubscribed = "unsubscribed";
    public const string Subscribed = "subscribed";
    public const string Spam = "spam";
    public const string SpamBlock = "spam_block";
    public const string Failed = "failed";

    public static readonly IReadOnlyCollection<string> All =
    [
        Pending,
        Queued,
        Retrying,
        Simulated,
        Accepted,
        Sent,
        Delivered,
        Opened,
        Clicked,
        SoftBounced,
        HardBounced,
        Unsubscribed,
        Subscribed,
        Spam,
        SpamBlock,
        Failed
    ];
}

public static class EmailProviders
{
    public const string NotificationService = "notification-service";
}
