using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Контракт чтения данных о работах по обслуживанию. </summary>
public interface IMaintenanceWorkQueryService
{
    /// <summary> Возвращает работы, назначенные исполнителю. </summary>
    Task<IReadOnlyList<MaintenanceWorkItemDto>> GetAssignedAsync(string userId, Guid organizationId);

    /// <summary> Возвращает доступные исполнителю работы. </summary>
    Task<IReadOnlyList<MaintenanceWorkItemDto>> GetAvailableAsync(string userId, Guid organizationId);

    /// <summary> Возвращает ход выполнения работ по обслуживанию. </summary>
    Task<IReadOnlyList<MaintenanceWorkProgressDto>> GetProgressAsync(
        string userId, Guid organizationId, Guid maintenanceId);

    /// <summary> Возвращает отчёты по работам обслуживания. </summary>
    Task<IReadOnlyList<MaintenanceWorkReportDto>> GetReportsAsync(
        string userId, Guid organizationId, Guid maintenanceId, Guid? detailId = null);

    /// <summary> Формирует производственный отчёт по обслуживанию. </summary>
    Task<MaintenanceProductionReportDto> GetProductionReportAsync(
        string userId, Guid organizationId, Guid maintenanceId);
}