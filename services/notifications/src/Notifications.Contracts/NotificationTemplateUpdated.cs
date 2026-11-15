namespace Notifications.Contracts;

/// <summary>Подтверждает сохранение шаблона перед отправкой использующих его уведомлений.</summary>
/// <param name="EventId">Идентификатор события.</param>
/// <param name="ApplicationId">Владелец шаблона.</param>
/// <param name="Key">Ключ шаблона.</param>
/// <param name="Channel">Канал доставки.</param>
/// <param name="OccurredAt">Момент обновления.</param>
/// <param name="Version">Версия сохранённого шаблона.</param>
public sealed record NotificationTemplateUpdated(
    Guid EventId,
    string ApplicationId,
    string Key,
    string Channel,
    DateTimeOffset OccurredAt,
    int Version = 1);
