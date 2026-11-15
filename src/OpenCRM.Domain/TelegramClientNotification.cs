namespace OpenCRM.Domain;

/// <summary> Уведомление клиента через Telegram. </summary>
public sealed class TelegramClientNotification
{
    /// <summary> Идентификатор Telegram-уведомления. </summary>
    public long Id { get; set; }

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Идентификатор клиента — получателя уведомления. </summary>
    public Guid ClientId { get; set; }

    /// <summary> Идентификатор чата Telegram для доставки. </summary>
    public long ChatId { get; set; }

    /// <summary> Идентификатор записи об обслуживании, к которой относится уведомление. </summary>
    public Guid MaintenanceId { get; set; }

    /// <summary> Версия записи об обслуживании на момент уведомления. </summary>
    public uint MaintenanceVersion { get; set; }

    /// <summary> Начало визита, для которого сформировано напоминание. </summary>
    public DateTime? VisitStartsAtUtc { get; set; }

    /// <summary> Код события, породившего уведомление. </summary>
    public string EventCode { get; set; } = string.Empty;

    /// <summary> Ключ идентификации уведомления. </summary>
    public string NotificationKey { get; set; } = string.Empty;

    /// <summary> Идентификатор команды уведомления. </summary>
    public Guid? NotificationCommandId { get; set; }

    /// <summary> Идентификатор уведомления. </summary>
    public Guid? NotificationId { get; set; }

    /// <summary> Дата и время отправки уведомления. </summary>
    public DateTime? NotificationDispatchedAt { get; set; }

    /// <summary> Дата и время события уведомления. </summary>
    public DateTime? NotificationEventAt { get; set; }

    /// <summary> Текст уведомления. </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary> Статус доставки уведомления. </summary>
    public string Status { get; set; } = "queued";

    /// <summary> Количество попыток отправки уведомления. </summary>
    public int AttemptCount { get; set; }

    /// <summary> Время следующей попытки отправки уведомления. </summary>
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время создания уведомления. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время успешной отправки уведомления. </summary>
    public DateTime? SentAt { get; set; }

    /// <summary> Идентификатор сообщения у провайдера Telegram. </summary>
    public string? ProviderMessageId { get; set; }

    /// <summary> Код ошибки доставки уведомления. </summary>
    public string? ErrorCode { get; set; }
}
