using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Контракт выполнения работ по обслуживанию. </summary>
public interface IMaintenanceWorkExecutionService
{
    /// <summary> Берёт работу в исполнение. </summary>
    Task<MaintenanceWorkItemDto> ClaimAsync(
        string userId, Guid organizationId, Guid maintenanceId, Guid detailId);

    /// <summary> Возвращает работу на доработку. </summary>
    Task ReturnForReworkAsync(
        string userId, Guid organizationId, Guid maintenanceId, Guid detailId, MaintenanceWorkCommandDto command);

    /// <summary> Выполняет команду над работой обслуживания. </summary>
    Task<MaintenanceWorkItemDto> ExecuteAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        MaintenanceWorkCommandDto command);

    /// <summary> Завершает работу за назначенного сотрудника с административным аудитом. </summary>
    Task<MaintenanceWorkItemDto> CompleteForEmployeeAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        MaintenanceWorkCommandDto command);
}
