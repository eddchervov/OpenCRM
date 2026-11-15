namespace OpenCRM.Domain;

/// <summary> Исходящая очередь Telegram-уведомлений. </summary>
public sealed class TelegramNotificationOutbox
{
    /// <summary> Идентификатор записи очереди. </summary>
    public long Id { get; set; }

    /// <summary> Ключ идентификации уведомления. </summary>
    public string NotificationKey { get; set; } = string.Empty;

    /// <summary> Текст уведомления. </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary> Статус уведомления. </summary>
    public string? NotificationStatus { get; set; }

    /// <summary> Идентификатор команды уведомления. </summary>
    public Guid? NotificationCommandId { get; set; }

    /// <summary> Идентификатор уведомления. </summary>
    public Guid? NotificationId { get; set; }

    /// <summary> Идентификатор сообщения у провайдера Telegram. </summary>
    public string? ProviderMessageId { get; set; }

    /// <summary> Код ошибки доставки. </summary>
    public string? ErrorCode { get; set; }

    /// <summary> Количество попыток отправки. </summary>
    public int AttemptCount { get; set; }

    /// <summary> Дата и время события уведомления. </summary>
    public DateTime? NotificationEventAt { get; set; }

    /// <summary> Дата и время отправки уведомления. </summary>
    public DateTime? NotificationDispatchedAt { get; set; }

    /// <summary> Время следующей обработки уведомления. </summary>
    public DateTime NextDispatchAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время создания записи очереди. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
