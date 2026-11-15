using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Контракт назначения исполнителей на обслуживания. </summary>
public interface IMaintenanceAssignmentService
{
    /// <summary> Назначает сотрудника исполнителем обслуживания. </summary>
    Task AssignEmployee(string userId, Guid organizationId, Guid maintenanceId, AssignMaintenanceEmployeeDto dto);
}
