using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Контракт изменения рабочего процесса записи об обслуживании. </summary>
public interface IMaintenanceWorkflowService
{
    /// <summary> Изменяет рабочий процесс записи об обслуживании для пользователя. </summary>
    Task ChangeWorkflow(string userId, Guid maintenanceId, MaintenanceWorkflowActionDto dto);

    /// <summary> Изменяет рабочий процесс записи в рамках организации с проверкой доступа. </summary>
    Task ChangeWorkflow(string userId, Guid? organizationId, Guid maintenanceId, MaintenanceWorkflowActionDto dto);
}
