using System.Text.Json;

using Notifications.Contracts;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>
/// Определяет операции хранения и конкурентного захвата уведомлений.
/// </summary>
public interface INotificationRepository
{
    /// <summary>Возвращает уведомление по идентификатору вместе с историей попыток.</summary>
    Task<Notification?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Атомарно захватывает ожидающее уведомление для единственного обработчика.</summary>
    Task<Notification?> ClaimAsync(
        Guid id,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Ищет ранее созданное уведомление по приложению и ключу идемпотентности.</summary>
    Task<Notification?> GetByIdempotencyKeyAsync(
        string applicationId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>Добавляет новое уведомление в текущую единицу работы.</summary>
    void Add(Notification notification);

    /// <summary>Добавляет запись о попытке доставки в текущую единицу работы.</summary>
    void AddAttempt(NotificationAttempt attempt);
}
