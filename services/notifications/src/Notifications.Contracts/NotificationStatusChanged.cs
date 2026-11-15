namespace Notifications.Contracts;

/// <summary>Передаёт состояние в очередь приложения. Принятие провайдером не подтверждает доставку получателю.</summary>
/// <param name="EventId">Идентификатор события для устранения повторов на стороне клиента.</param>
/// <param name="ApplicationId">Владелец уведомления.</param>
/// <param name="IdempotencyKey">Ключ исходного задания, известный клиенту до отправки.</param>
/// <param name="NotificationId">Идентификатор записи; отсутствует при ответе not-found.</param>
/// <param name="Status">queued, processing, retrying, provider-accepted, simulated, failed или not-found.</param>
/// <param name="AttemptCount">Номер последней начатой попытки.</param>
/// <param name="OccurredAt">Момент формирования снимка.</param>
/// <param name="ProviderMessageId">Идентификатор или подтверждение провайдера; для имитации имеет префикс log:.</param>
/// <param name="ErrorCode">Стабильный код ошибки без текста исключений и секретов.</param>
/// <param name="Content">Сохранённое содержимое, доступное только приложению-владельцу.</param>
public sealed record NotificationStatusChanged(
    Guid EventId,
    string ApplicationId,
    string IdempotencyKey,
    Guid? NotificationId,
    string Status,
    int AttemptCount,
    DateTimeOffset OccurredAt,
    string? ProviderMessageId = null,
    string? ErrorCode = null,
    NotificationContent? Content = null);
