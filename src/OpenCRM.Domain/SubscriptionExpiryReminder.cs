namespace OpenCRM.Domain;

/// <summary> Напоминание об истечении подписки. </summary>
public class SubscriptionExpiryReminder
{
    /// <summary> Идентификатор напоминания. </summary>
    public long Id { get; set; }

    /// <summary> Код области действия напоминания. </summary>
    public string ScopeCode { get; set; } = string.Empty;

    /// <summary> Идентификатор подписки. </summary>
    public int SubscriptionId { get; set; }

    /// <summary> Идентификатор пользователя-получателя. </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary> Идентификатор организации. </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary> Дата окончания подписки. </summary>
    public DateTime SubscriptionEndDate { get; set; }

    /// <summary> Код события напоминания. </summary>
    public string EventCode { get; set; } = string.Empty;

    /// <summary> Email-адрес получателя напоминания. </summary>
    public string RecipientEmail { get; set; } = string.Empty;

    /// <summary> Дата и время создания напоминания. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время последней попытки отправки. </summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary> Количество попыток отправки напоминания. </summary>
    public int AttemptCount { get; set; }

    /// <summary> Дата и время постановки уведомления в очередь. </summary>
    public DateTime? NotificationQueuedAt { get; set; }

    /// <summary> Дата и время отправки напоминания. </summary>
    public DateTime? SentAt { get; set; }

    /// <summary> Сообщение об ошибке отправки напоминания. </summary>
    public string? ErrorMessage { get; set; }
}
