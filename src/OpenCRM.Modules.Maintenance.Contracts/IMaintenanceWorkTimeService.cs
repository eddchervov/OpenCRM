using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Контракт корректировки рабочего времени работ. </summary>
public interface IMaintenanceWorkTimeService
{
    /// <summary> Корректирует рабочее время по работе. </summary>
    Task<MaintenanceWorkReportDto> AdjustTimeAsync(
        string userId, Guid organizationId, Guid maintenanceId, Guid detailId, AdjustMaintenanceWorkTimeDto command);
}
