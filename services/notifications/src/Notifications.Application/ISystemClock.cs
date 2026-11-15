using System.Text.Json;

using Notifications.Contracts;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>
/// Абстрагирует системное время для воспроизводимой бизнес-логики и тестов.
/// </summary>
public interface ISystemClock
{
    /// <summary>Возвращает текущий момент времени в UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
