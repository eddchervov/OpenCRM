using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Контракт управления отчётами по работам. </summary>
public interface IMaintenanceWorkReportService
{
    /// <summary> Добавляет отчёт по работе обслуживания. </summary>
    Task<MaintenanceWorkReportDto> AddReportAsync(
        string userId, Guid organizationId, Guid maintenanceId, Guid detailId, CreateMaintenanceWorkReportDto command);

    /// <summary> Принимает решение по отчёту о работе. </summary>
    Task<MaintenanceWorkReportDto> DecideReportAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid reportId,
        DecideMaintenanceWorkReportDto command);
}
