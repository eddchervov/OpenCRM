namespace Notifications.Contracts;

/// <summary>Запрашивает снимок состояния в очередь результатов приложения.</summary>
/// <param name="IdempotencyKey">Ключ исходной команды создания уведомления.</param>
public sealed record GetNotificationStatus(string IdempotencyKey);
