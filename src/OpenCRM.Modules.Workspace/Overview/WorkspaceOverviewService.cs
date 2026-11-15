using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Domain;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Clients.Contracts;

namespace OpenCRM.Modules.Workspace.Overview;

/// <summary> Сервис формирования обзорной сводки рабочего пространства. </summary>
public sealed class WorkspaceOverviewService(
    IOrganizationQueryService organizationService,
    IWorkspaceAccessService workspaceAccessService,
    IOrganizationWorkspaceQueryService organizationWorkspaceQueryService,
    IOrganizationClientCountQueryService clientCounts,
    IOrganizationEntitlementService organizationEntitlementService,
    IVehicleQueryService vehicleService,
    IVehicleLimitQueryService vehicleLimitService,
    ISystemClock systemClock) : IWorkspaceOverviewService
{
    /// <summary> Возвращает обзорные данные рабочего пространства организации. </summary>
    public async Task<WorkspaceOverviewResultDto> GetAsync(string userId, Guid organizationId)
    {
        var team = await organizationService.GetOverviewAsync(userId);
        var organization = team.Organizations.FirstOrDefault(item => item.Id == organizationId);
        if (organization == null)
        {
            return new WorkspaceOverviewResultDto();
        }

        var access = await workspaceAccessService.GetOverviewAsync(userId, organizationId);
        var organizationTimeZone = string.IsNullOrWhiteSpace(organization.TimeZoneId)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(organization.TimeZoneId);
        var organizationToday = TimeZoneInfo.ConvertTimeFromUtc(systemClock.UtcNow, organizationTimeZone).Date;

        var planning = access.CanViewAnalytics
            ? await organizationWorkspaceQueryService.GetPlanningSummaryAsync(
                organizationId,
                access.HasDefectsModule,
                access.HasCountersModule,
                access.HasSchedulerModule,
                organizationToday,
                organizationTimeZone)
            : new OrganizationWorkspacePlanningSummary();

        var metrics = access.CanViewAnalytics
            ? await organizationWorkspaceQueryService.GetOverviewMetricsAsync(
                organizationId,
                access.HasDefectsModule,
                access.HasCountersModule)
            : new OrganizationWorkspaceOverviewMetrics();

        var overview = new OrganizationWorkspaceOverviewDto
        {
            Organization = organization,
            WorkspaceAccess = access.WorkspaceAccess,
            WorkspaceFeatures = access.FeatureSnapshot.Features,
            CurrentSubscription = await organizationEntitlementService.GetCurrentAsync(userId, organizationId),
            EffectivePlan = await organizationEntitlementService.GetEffectivePlanAsync(userId, organizationId),
            WorkspaceVehicleLimit = access.CanViewVehicles
                ? (await vehicleLimitService.GetVehicleLimitInfoAsync(userId, organizationId)).MaxVehiclesForUser
                : 0,
            WorkspaceMembersLimit = await organizationEntitlementService.GetMembersLimitAsync(userId, organizationId, Helpers.PlatformDefaults.WorkspaceMembersLimit),
            ActiveVehiclesCount = access.CanViewVehicles ? await vehicleService.GetVehiclesCountAsync(userId, organizationId) : 0,
            ArchivedVehiclesCount = access.CanViewVehicles ? await vehicleService.GetArchivedVehiclesCountAsync(userId, organizationId) : 0,
            ClientsCount = access.CanViewClients ? await clientCounts.GetActiveCountAsync(organizationId) : 0,
            Metrics = metrics,
            Planning = planning
        };

        return new WorkspaceOverviewResultDto
        {
            Overview = overview
        };
    }
}
