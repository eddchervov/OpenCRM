using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary>
/// Возвращает аналитику и показатели рабочего пространства организации.
/// </summary>
public interface IOrganizationWorkspaceAnalyticsQueryService
{
    /// <summary>
    /// Возвращает сценарную аналитику рабочего пространства.
    /// </summary>
    Task<OrganizationWorkspaceAnalytics> GetAnalyticsAsync(
        Guid organizationId,
        bool hasClientsModule,
        bool hasDefectsModule,
        bool hasCountersModule,
        bool hasMaterialsModule,
        DateTime today);

    /// <summary>
    /// Возвращает ключевые метрики главного экрана организации.
    /// </summary>
    Task<OrganizationWorkspaceOverviewMetrics> GetOverviewMetricsAsync(
        Guid organizationId,
        bool hasDefectsModule,
        bool hasCountersModule);
}
