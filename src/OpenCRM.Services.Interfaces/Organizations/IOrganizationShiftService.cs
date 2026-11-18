using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary> Контракт сервиса управления сменами организации. </summary>
public interface IOrganizationShiftService
{
    /// <summary> Возвращает смены организации за неделю. </summary>
    Task<OrganizationShiftWeekDto> GetWeekAsync(string userId, Guid organizationId, DateOnly weekStart);

    /// <summary> Возвращает смены текущего пользователя в организации. </summary>
    Task<IReadOnlyList<OrganizationShiftDto>> GetOwnAsync(string userId, Guid organizationId);

    /// <summary> Определяет доступность смен сотрудников организации за период. </summary>
    Task<OrganizationShiftAvailabilityDto> GetAvailabilityAsync(
        string userId,
        Guid organizationId,
        Guid employeeId,
        string startLocal,
        string endLocal);

    /// <summary> Задаёт часовой пояс организации для смен. </summary>
    Task SetTimeZoneAsync(string userId, Guid organizationId, string timeZoneId, string? expectedTimeZoneId = null);

    /// <summary> Сохраняет смену организации, создавая или обновляя запись. </summary>
    Task<Guid> SaveAsync(string userId, Guid organizationId, Guid? shiftId, SaveOrganizationShiftDto dto);

    /// <summary> Отменяет смену организации с проверкой версии. </summary>
    Task CancelAsync(string userId, Guid organizationId, Guid shiftId, uint version, string? reason);

    /// <summary> Публикует смены на неделю. </summary>
    Task<int> PublishWeekAsync(string userId, Guid organizationId, DateOnly weekStart);

    /// <summary> Копирует смены предыдущей недели на заданную неделю. </summary>
    Task<int> CopyPreviousWeekAsync(string userId, Guid organizationId, DateOnly weekStart);
}

