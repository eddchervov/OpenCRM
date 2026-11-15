namespace Notifications.Domain;

/// <summary>
/// Хранит неизменяемый журнал одной попытки передачи уведомления провайдеру.
/// </summary>
public sealed class NotificationAttempt
{
    /// <summary> Создаёт экземпляр для инфраструктуры. </summary>
    private NotificationAttempt()
    {
    }

    /// <summary>Создаёт запись о завершённой попытке доставки.</summary>
    public NotificationAttempt(
        Guid notificationId,
        int number,
        DateTimeOffset startedAt,
        bool succeeded,
        string? providerMessageId,
        string? error)
    {
        this.Id = Guid.NewGuid();
        this.NotificationId = notificationId;
        this.Number = number;
        this.StartedAt = startedAt;
        this.Succeeded = succeeded;
        this.ProviderMessageId = providerMessageId;
        this.Error = error;
    }

    /// <summary>Уникальный идентификатор записи журнала.</summary>
    public Guid Id { get; private set; }

    /// <summary>Идентификатор уведомления, к которому относится попытка.</summary>
    public Guid NotificationId { get; private set; }

    /// <summary>Порядковый номер попытки доставки.</summary>
    public int Number { get; private set; }

    /// <summary>Момент начала попытки.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Показывает, завершилась ли попытка успешно.</summary>
    public bool Succeeded { get; private set; }

    /// <summary>Идентификатор или ответ внешнего провайдера при успешной отправке.</summary>
    public string? ProviderMessageId { get; private set; }

    /// <summary>Текст ошибки неуспешной попытки.</summary>
    public string? Error { get; private set; }
}
