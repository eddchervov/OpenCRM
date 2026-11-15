using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Контракт планирования обслуживаний. </summary>
public interface IMaintenanceSchedulingService
{
    /// <summary> Обновляет плановую дату обслуживания и возвращает новый статус. </summary>
    Task<MaintenanceStatus> UpdatePlannedDate(
        string userId,
        Guid? organizationId,
        Guid maintenanceId,
        UpdateMaintenancePlannedDateDto dto);
}
