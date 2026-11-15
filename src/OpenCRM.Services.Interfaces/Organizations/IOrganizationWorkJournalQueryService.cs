using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary>
/// Возвращает журнал завершенных работ и историю организации.
/// </summary>
public interface IOrganizationWorkJournalQueryService
{
    /// <summary>
    /// Возвращает журнал работ с фильтрами по технике, типу и вкладке.
    /// </summary>
    Task<OrganizationWorkspaceWorkJournalResult> GetWorkJournalAsync(
        Guid organizationId,
        DateTime dateFrom,
        DateTime dateTo,
        Guid? vehicleId,
        string? kind,
        string? tab,
        bool hasDefectsModule,
        TimeZoneInfo? timeZone = null);
}
