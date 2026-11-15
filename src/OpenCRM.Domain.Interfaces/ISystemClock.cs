namespace OpenCRM.Domain.Interfaces;

/// <summary> Контракт доступа к текущему времени системы. </summary>
public interface ISystemClock
{
    /// <summary> Текущее время в UTC. </summary>
    public DateTime UtcNow { get; }

    /// <summary> Текущая дата в UTC. </summary>
    public DateTime UtcToday { get; }
}

