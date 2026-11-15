namespace Notifications.Contracts;

/// <summary>Команда создания уведомления. Приложение определяется входной очередью, а не телом сообщения.</summary>
/// <param name="Request">Данные уведомления и постоянный ключ идемпотентности.</param>
public sealed record SendNotification(CreateNotificationRequest Request);
