namespace OpenCRM.Modules.Planning.Contracts;

/// <summary> Контракт команд workflow задач планировщика. </summary>
public interface ISchedulerWorkflowCommandService
{
    /// <summary> Обновляет статус workflow задачи планировщика. </summary>
    Task UpdateTaskWorkflowAsync(string userId, Guid organizationId, Guid vehicleId, string sourceCode, Guid? sourceEntityId, string triggerKey, string statusCode);

    /// <summary> Откладывает задачу и возвращает новое время, если есть. </summary>
    Task<DateTime?> DeferTaskAsync(string userId, Guid organizationId, Guid vehicleId, string sourceCode, Guid? sourceEntityId, string triggerKey, DateTime? deferredUntil, DateTime today);
}
