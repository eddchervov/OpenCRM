namespace OpenCRM.Modules.Planning.Contracts;

/// <summary>Внутренний участник транзакции обслуживания. Вызывающая сторона проверяет права и блокирует организацию.</summary>
public interface IMaintenanceSchedulerSyncService
{
    /// <summary> Связывает задачу планировщика с обслуживанием. </summary>
    Task LinkMaintenanceTaskAsync(string userId, Guid organizationId, Guid vehicleId, Guid maintenanceId, string sourceCode, Guid? sourceEntityId, string triggerKey, int maintenanceStatus);

    /// <summary> Синхронизирует workflow задачи с состоянием обслуживания. </summary>
    Task SyncMaintenanceWorkflowAsync(string userId, Guid organizationId, Guid maintenanceId, int maintenanceStatus);

    /// <summary> Сбрасывает workflow задачи обслуживания. </summary>
    Task ResetMaintenanceWorkflowAsync(string userId, Guid organizationId, Guid maintenanceId);
}
