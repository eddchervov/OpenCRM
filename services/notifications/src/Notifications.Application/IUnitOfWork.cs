using System.Text.Json;

using Notifications.Contracts;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>
/// Объединяет изменения уведомления, журнала и outbox-сообщений в одну транзакцию.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Атомарно сохраняет накопленные изменения.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
