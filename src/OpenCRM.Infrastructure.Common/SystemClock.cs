using OpenCRM.Domain.Interfaces;

namespace OpenCRM.Infrastructure.Common;

/// <summary> Реализация системных часов. </summary>
public class SystemClock : ISystemClock
{
    /// <summary> Текущее время в UTC. </summary>
    public DateTime UtcNow => DateTime.UtcNow;

    /// <summary> Текущая дата в UTC. </summary>
    public DateTime UtcToday => DateTime.UtcNow.Date;
}

