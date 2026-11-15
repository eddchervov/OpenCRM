using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Workspace.Queries;

/// <summary> Агрегирующий сервис данных рабочего пространства организации. </summary>
public class OrganizationWorkspaceQueryService(
    IOrganizationWorkdayQueryService organizationWorkdayQueryService,
    IOrganizationWorkspaceMaintenanceQueryService organizationWorkspaceMaintenanceQueryService,
    IOrganizationFinanceQueryService organizationFinanceQueryService,
    IOrganizationMaintenanceJournalQueryService organizationMaintenanceJournalQueryService,
    IOrganizationWorkJournalQueryService organizationWorkJournalQueryService,
    IOrganizationWorkspaceAnalyticsQueryService organizationWorkspaceAnalyticsQueryService,
    IOrganizationWorkspacePlanningQueryService organizationWorkspacePlanningQueryService) : IOrganizationWorkspaceQueryService
{
    /// <summary> Возвращает данные рабочего дня организации. </summary>
    public Task<OrganizationWorkdayDto> GetWorkdayAsync(Guid organizationId, DateTime date, bool canViewClients, TimeZoneInfo? timeZone = null)
    {
        return organizationWorkdayQueryService.GetWorkdayAsync(organizationId, date, canViewClients, timeZone);
    }

    /// <summary> Возвращает визуализацию рабочего дня организации. </summary>
    public Task<OrganizationWorkdayDto> GetVisualizationAsync(
        Guid organizationId,
        DateTime date,
        bool canViewClients,
        TimeZoneInfo? timeZone = null)
    {
        return organizationWorkdayQueryService.GetVisualizationAsync(organizationId, date, canViewClients, timeZone);
    }

    /// <summary> Возвращает обзорные показатели рабочего пространства. </summary>
    public Task<OrganizationWorkspaceOverviewMetrics> GetOverviewMetricsAsync(
        Guid organizationId,
        bool hasDefectsModule,
        bool hasCountersModule)
    {
        return organizationWorkspaceAnalyticsQueryService.GetOverviewMetricsAsync(
            organizationId,
            hasDefectsModule,
            hasCountersModule);
    }

    /// <summary> Возвращает аналитику рабочего пространства. </summary>
    public Task<OrganizationWorkspaceAnalytics> GetAnalyticsAsync(
        Guid organizationId,
        bool hasClientsModule,
        bool hasDefectsModule,
        bool hasCountersModule,
        bool hasMaterialsModule,
        DateTime today)
    {
        return organizationWorkspaceAnalyticsQueryService.GetAnalyticsAsync(
            organizationId,
            hasClientsModule,
            hasDefectsModule,
            hasCountersModule,
            hasMaterialsModule,
            today);
    }

    /// <summary> Возвращает сводку планирования рабочего пространства. </summary>
    public Task<OrganizationWorkspacePlanningSummary> GetPlanningSummaryAsync(
        Guid organizationId,
        bool hasDefectsModule,
        bool hasCountersModule,
        bool hasSchedulerModule,
        DateTime today,
        TimeZoneInfo? timeZone = null)
    {
        return organizationWorkspacePlanningQueryService.GetPlanningSummaryAsync(
            organizationId,
            hasDefectsModule,
            hasCountersModule,
            hasSchedulerModule,
            today,
            timeZone);
    }

    /// <summary> Возвращает финансовую сводку организации за период. </summary>
    public Task<OrganizationFinanceSummaryDto> GetFinanceSummaryAsync(
        Guid organizationId,
        DateTime dateFrom,
        DateTime dateTo)
    {
        return organizationFinanceQueryService.GetFinanceSummaryAsync(organizationId, dateFrom, dateTo);
    }

    /// <summary> Возвращает постраничную ленту обслуживаний организации. </summary>
    public Task<OrganizationWorkspaceMaintenanceFeedResult> GetMaintenancesAsync(
        Guid organizationId,
        DateTime dateFrom,
        DateTime dateTo,
        int page,
        int pageSize)
    {
        return organizationWorkspaceMaintenanceQueryService.GetMaintenancesAsync(
            organizationId,
            dateFrom,
            dateTo,
            page,
            pageSize);
    }

    /// <summary> Возвращает обслуживаний для календаря организации. </summary>
    public Task<IReadOnlyList<OrganizationRecentMaintenanceDto>> GetMaintenanceCalendarAsync(
        Guid organizationId,
        DateTime dateFrom,
        DateTime dateTo,
        Guid? vehicleId = null,
        bool includeAllStatuses = false)
    {
        return organizationWorkspaceMaintenanceQueryService.GetMaintenanceCalendarAsync(
            organizationId,
            dateFrom,
            dateTo,
            vehicleId,
            includeAllStatuses);
    }

    /// <summary> Возвращает журнал обслуживаний организации. </summary>
    public Task<OrganizationMaintenanceJournalResult> GetMaintenanceJournalAsync(
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
        bool canViewPayments = false)
    {
        return organizationMaintenanceJournalQueryService.GetMaintenanceJournalAsync(
            organizationId,
            status,
            dateFrom,
            dateTo,
            search,
            page,
            pageSize,
            canViewClients,
            cancellationToken,
            handover,
            employeeId,
            canViewPayments);
    }

    /// <summary> Возвращает журнал работ организации. </summary>
    public Task<OrganizationWorkspaceWorkJournalResult> GetWorkJournalAsync(
        Guid organizationId,
        DateTime dateFrom,
        DateTime dateTo,
        Guid? vehicleId,
        string? kind,
        string? tab,
        bool hasDefectsModule,
        TimeZoneInfo? timeZone = null)
    {
        return organizationWorkJournalQueryService.GetWorkJournalAsync(
            organizationId,
            dateFrom,
            dateTo,
            vehicleId,
            kind,
            tab,
            hasDefectsModule,
            timeZone);
    }
}
