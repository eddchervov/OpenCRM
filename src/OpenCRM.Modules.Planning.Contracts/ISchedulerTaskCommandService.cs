using OpenCRM.Modules.Planning.Contracts.Dtos;

namespace OpenCRM.Modules.Planning.Contracts;

/// <summary>Команды ручных задач с проверкой доступа и атомарным сохранением задачи, повторов и аудита.</summary>
public interface ISchedulerTaskCommandService
{
    /// <summary> Создаёт ручную задачу планировщика организации. </summary>
    Task<Guid> CreateTaskAsync(string userId, Guid organizationId, CreateOrganizationSchedulerTaskDto dto);

    /// <summary> Обновляет данные ручной задачи. </summary>
    Task UpdateManualTaskAsync(string userId, Guid organizationId, Guid taskId, UpdateOrganizationSchedulerTaskDto dto);

    /// <summary> Обновляет статус ручной задачи. </summary>
    Task UpdateManualTaskStatusAsync(string userId, Guid organizationId, Guid taskId, string statusCode);

    /// <summary> Обновляет расписание ручной задачи. </summary>
    Task UpdateManualTaskScheduleAsync(string userId, Guid organizationId, Guid taskId, UpdateOrganizationSchedulerTaskScheduleDto dto);

    /// <summary> Удаляет ручную задачу планировщика. </summary>
    Task DeleteManualTaskAsync(string userId, Guid organizationId, Guid taskId);

    /// <summary> Отмечает напоминание задачи отправленным. </summary>
    Task MarkReminderSentAsync(string userId, Guid organizationId, Guid taskId, DateTime sentAt);
}
