using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary>
/// Собирает агрегированные данные рабочего пространства организации.
/// </summary>
public interface IOrganizationWorkspaceQueryService
{
    /// <summary>Возвращает рабочий день детейлинга, включая незакрытые работы прошлых дней.</summary>
    Task<OrganizationWorkdayDto> GetWorkdayAsync(Guid organizationId, DateTime date, bool canViewClients, TimeZoneInfo? timeZone = null);

    /// <summary>Возвращает все незакрытые записи для визуализации постов без ограничения размера групп.</summary>
    Task<OrganizationWorkdayDto> GetVisualizationAsync(
        Guid organizationId,
        DateTime date,
        bool canViewClients,
        TimeZoneInfo? timeZone = null);

    /// <summary>
    /// Возвращает сводку планирования с учетом подключенных модулей.
    /// </summary>
    Task<OrganizationWorkspacePlanningSummary> GetPlanningSummaryAsync(
        Guid organizationId,
        bool hasDefectsModule,
        bool hasCountersModule,
        bool hasSchedulerModule,
        DateTime today,
        TimeZoneInfo? timeZone = null);

    /// <summary>
    /// Возвращает ключевые метрики главного экрана организации.
    /// </summary>
    Task<OrganizationWorkspaceOverviewMetrics> GetOverviewMetricsAsync(
        Guid organizationId,
        bool hasDefectsModule,
        bool hasCountersModule);

    /// <summary>
    /// Возвращает сценарную аналитику для современных клиентских рабочих пространств.
    /// </summary>
    Task<OrganizationWorkspaceAnalytics> GetAnalyticsAsync(
        Guid organizationId,
        bool hasClientsModule,
        bool hasDefectsModule,
        bool hasCountersModule,
        bool hasMaterialsModule,
        DateTime today);

    /// <summary>
    /// Возвращает финансовую сводку по заказам/обслуживаниям организации за период.
    /// </summary>
    Task<OrganizationFinanceSummaryDto> GetFinanceSummaryAsync(
        Guid organizationId,
        DateTime dateFrom,
        DateTime dateTo);

    /// <summary>
    /// Возвращает постраничную ленту обслуживаний для рабочего пространства.
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

    /// <summary> Возвращает страницу обслуживаний за любой период и в любом статусе. </summary>
    Task<OrganizationMaintenanceJournalResult> GetMaintenanceJournalAsync(
        Guid organizationId,
        MaintenanceStatus? status,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? search,
        int page,
        int pageSize,
        bool canViewClients,
        CancellationToken cancellationToken = default,
        string? handover = null,
        Guid? employeeId = null,
        bool canViewPayments = false);

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
