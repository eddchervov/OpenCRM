namespace OpenCRM.Modules.Planning.Contracts;

/// <summary> Контракт генерации повторяющихся задач планировщика. </summary>
public interface ISchedulerRecurrenceService
{
    /// <summary> Создаёт экземпляры повторяющихся задач до заданной даты. </summary>
    Task<int> GenerateRecurringTaskInstancesAsync(string userId, Guid organizationId, DateTime horizonDate);
}
