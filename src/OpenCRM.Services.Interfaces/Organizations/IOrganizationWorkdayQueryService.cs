using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary>
/// Возвращает данные рабочего дня организации и представления постов.
/// </summary>
public interface IOrganizationWorkdayQueryService
{
    /// <summary>
    /// Возвращает рабочий день организации с учетом часового пояса.
    /// </summary>
    Task<OrganizationWorkdayDto> GetWorkdayAsync(
        Guid organizationId,
        DateTime date,
        bool canViewClients,
        TimeZoneInfo? timeZone = null);

    /// <summary>
    /// Возвращает все незакрытые записи для визуализации постов без ограничения размера групп.
    /// </summary>
    Task<OrganizationWorkdayDto> GetVisualizationAsync(
        Guid organizationId,
        DateTime date,
        bool canViewClients,
        TimeZoneInfo? timeZone = null);
}
