using System.Text.Json;

namespace Notifications.Contracts;

/// <summary>
/// Возвращает клиенту текущее состояние созданного уведомления.
/// </summary>
/// <param name="Id">Уникальный идентификатор уведомления.</param>
/// <param name="ApplicationId">Идентификатор приложения-владельца уведомления.</param>
/// <param name="Channel">Канал доставки.</param>
/// <param name="Recipient">Получатель уведомления.</param>
/// <param name="Status">Текущее состояние обработки.</param>
/// <param name="AttemptCount">Количество начатых попыток доставки.</param>
/// <param name="CreatedAt">Момент создания уведомления.</param>
/// <param name="ScheduledAt">Запланированный момент отправки.</param>
/// <param name="SentAt">Момент успешной доставки провайдеру.</param>
/// <param name="LastError">Текст последней ошибки доставки.</param>
/// <param name="WasDuplicate">Признак возврата ранее созданного уведомления по ключу идемпотентности.</param>
public sealed record NotificationResponse(
    Guid Id,
    string ApplicationId,
    string Channel,
    string Recipient,
    string Status,
    int AttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? SentAt,
    string? LastError,
    bool WasDuplicate = false);
