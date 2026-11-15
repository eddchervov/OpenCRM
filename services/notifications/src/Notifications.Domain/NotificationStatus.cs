namespace Notifications.Domain;

/// <summary>
/// Описывает жизненный цикл уведомления.
/// </summary>
public enum NotificationStatus
{
    /// <summary>Уведомление ожидает публикации или повторной попытки.</summary>
    Queued = 1,

    /// <summary>Уведомление захвачено одним обработчиком и сейчас отправляется.</summary>
    Processing = 2,

    /// <summary>Провайдер успешно принял уведомление.</summary>
    Sent = 3,

    /// <summary>Все разрешённые попытки исчерпаны.</summary>
    Failed = 4
}
