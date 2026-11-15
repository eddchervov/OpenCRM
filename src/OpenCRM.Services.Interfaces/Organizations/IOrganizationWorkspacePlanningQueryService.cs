using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary>
/// Возвращает сводку планирования рабочего пространства организации.
/// </summary>
public interface IOrganizationWorkspacePlanningQueryService
{
    /// <summary>
    /// Возвращает состояние планирования с учетом подключенных модулей и даты.
    /// </summary>
    Task<OrganizationWorkspacePlanningSummary> GetPlanningSummaryAsync(
        Guid organizationId,
        bool hasDefectsModule,
        bool hasCountersModule,
        bool hasSchedulerModule,
        DateTime today,
        TimeZoneInfo? timeZone = null);
}
