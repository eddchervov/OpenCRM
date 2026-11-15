using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Контракт создания записей об обслуживании. </summary>
public interface IMaintenanceCreateService
{
    /// <summary> Создаёт обслуживание для транспортного средства. </summary>
    Task<Guid> Create(string userId, Guid vehicleId, CreateMaintenanceDto dto);

    /// <summary> Создаёт обслуживание в рамках организации или без неё. </summary>
    Task<Guid> Create(string userId, Guid? organizationId, Guid vehicleId, CreateMaintenanceDto dto);

    /// <summary> Создаёт обслуживание со связью с источником в планировщике. </summary>
    Task<Guid> Create(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        CreateMaintenanceDto dto,
        MaintenanceSchedulerLink? schedulerLink);
}
