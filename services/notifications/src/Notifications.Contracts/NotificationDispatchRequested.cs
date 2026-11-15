using System.Text.Json;

namespace Notifications.Contracts;

/// <summary>
/// Событие RabbitMQ, запрашивающее попытку доставки конкретного уведомления.
/// </summary>
/// <param name="NotificationId">Идентификатор уведомления в базе сервиса.</param>
public sealed record NotificationDispatchRequested(Guid NotificationId);
