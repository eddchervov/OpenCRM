namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Сводка по обслуживаниям клиента. </summary>
public sealed class MaintenanceClientSummaryDto
{
    /// <summary> Количество обслуживаний клиента. </summary>
    public int Count { get; init; }

    /// <summary> Дата последнего обслуживания. </summary>
    public DateTime? LastDate { get; init; }
}
