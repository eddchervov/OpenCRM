using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Контракт удаления обслуживаний. </summary>
public interface IMaintenanceDeleteService
{
    /// <summary> Удаляет обслуживание. </summary>
    Task Delete(string userId, Guid maintenanceId);

    /// <summary> Удаляет обслуживание в контексте организации. </summary>
    Task Delete(string userId, Guid? organizationId, Guid maintenanceId);
}
