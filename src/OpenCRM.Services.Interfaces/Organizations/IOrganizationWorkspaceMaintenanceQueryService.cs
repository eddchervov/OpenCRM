using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary>
/// Возвращает ленту обслуживаний и события календаря рабочего пространства организации.
/// </summary>
public interface IOrganizationWorkspaceMaintenanceQueryService
{
    /// <summary>
    /// Возвращает постраничную ленту обслуживаний за период.
    /// </summary>
    Task<OrganizationWorkspaceMaintenanceFeedResult> GetMaintenancesAsync(
        Guid organizationId,
        DateTime dateFrom,
        DateTime dateTo,
        int page,
        int pageSize);

    /// <summary>
    /// Возвращает обслуживания в формате календарных событий.
    /// </summary>
    Task<IReadOnlyList<OrganizationRecentMaintenanceDto>> GetMaintenanceCalendarAsync(
        Guid organizationId,
        DateTime dateFrom,
        DateTime dateTo,
        Guid? vehicleId = null,
        bool includeAllStatuses = false);
}
