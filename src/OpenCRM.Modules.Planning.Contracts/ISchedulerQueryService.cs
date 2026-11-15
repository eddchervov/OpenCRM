using OpenCRM.Modules.Planning.Contracts.Dtos;

namespace OpenCRM.Modules.Planning.Contracts;

/// <summary> Контракт запросов планировщика организации. </summary>
public interface ISchedulerQueryService
{
    /// <summary> Возвращает наступившие напоминания планировщика. </summary>
    Task<IReadOnlyList<OrganizationSchedulerReminderDto>> GetDueRemindersAsync(string userId, Guid organizationId, DateTime now, int take = 20);

    /// <summary> Возвращает календарь ручных задач с фильтрами. </summary>
    Task<IReadOnlyList<OrganizationSchedulerCalendarItemDto>> GetManualTaskCalendarAsync(
        string userId, Guid organizationId, DateTime dateFrom, DateTime dateTo, Guid? vehicleId, Guid? clientId,
        string? responsibleUserId, string? taskType, string? priority);
}
