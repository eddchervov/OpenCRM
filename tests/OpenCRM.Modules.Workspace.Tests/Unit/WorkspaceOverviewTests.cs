using Moq;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Modules.Workspace.Overview;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Workspace.Tests.Unit;

public sealed class WorkspaceOverviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableWorkspaceDoesNotReadBusinessData(bool hasOrganization)
    {
        var organizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();
        var organizations = new Mock<IOrganizationQueryService>();
        organizations.Setup(service => service.GetOverviewAsync("actor")).ReturnsAsync(new TeamOverviewDto
        {
            Organizations = hasOrganization
                ? [new OrganizationDto { Id = otherOrganizationId }]
                : []
        });
        var access = new Mock<IWorkspaceAccessService>(MockBehavior.Strict);
        var service = new WorkspaceOverviewService(organizations.Object, access.Object,
            Mock.Of<IOrganizationWorkspaceQueryService>(MockBehavior.Strict),
            Mock.Of<IOrganizationClientCountQueryService>(MockBehavior.Strict),
            Mock.Of<IOrganizationEntitlementService>(MockBehavior.Strict),
            Mock.Of<IVehicleQueryService>(MockBehavior.Strict), Mock.Of<IVehicleLimitQueryService>(MockBehavior.Strict),
            Mock.Of<ISystemClock>());

        var result = await service.GetAsync("actor", organizationId);

        Assert.Null(result.Overview);
        access.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    public async Task OverviewReadsOnlyAllowedCountsAndPreservesSubscriptionLimits(
        bool canViewAnalytics, bool canViewVehicles, bool canViewClients)
    {
        var organizationId = Guid.NewGuid();
        var organization = new OrganizationDto { Id = organizationId, Name = "Workspace", TimeZoneId = "Asia/Novosibirsk" };
        var organizations = new Mock<IOrganizationQueryService>();
        organizations.Setup(service => service.GetOverviewAsync("actor"))
            .ReturnsAsync(new TeamOverviewDto { Organizations = [organization] });
        var allowed = new WorkspaceOverviewAccessDto
        {
            CanViewAnalytics = canViewAnalytics,
            CanViewVehicles = canViewVehicles,
            CanViewClients = canViewClients,
            HasDefectsModule = true,
            HasCountersModule = true,
            HasSchedulerModule = true,
            WorkspaceAccess = new OrganizationWorkspaceAccessDto { IsReadOnly = true }
        };
        var access = new Mock<IWorkspaceAccessService>();
        access.Setup(service => service.GetOverviewAsync("actor", organizationId)).ReturnsAsync(allowed);
        var today = new DateTime(2026, 10, 13);
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 10, 12, 20, 30, 0, DateTimeKind.Utc));
        var queries = new Mock<IOrganizationWorkspaceQueryService>();
        var planning = new OrganizationWorkspacePlanningSummary { OverdueCount = 7, SoonCount = 3 };
        var metrics = new OrganizationWorkspaceOverviewMetrics { MaintenanceTotalCount = 13, DocumentsCount = 4 };
        queries.Setup(service => service.GetPlanningSummaryAsync(organizationId, true, true, true, today, It.Is<TimeZoneInfo>(zone => zone.Id == "Asia/Novosibirsk"))).ReturnsAsync(planning);
        queries.Setup(service => service.GetOverviewMetricsAsync(organizationId, true, true)).ReturnsAsync(metrics);
        var clients = new Mock<IOrganizationClientCountQueryService>();
        clients.Setup(service => service.GetActiveCountAsync(organizationId)).ReturnsAsync(19);
        var entitlement = new Mock<IOrganizationEntitlementService>();
        var subscription = new OrganizationSubscriptionDto();
        var plan = new SubscriptionPlanDto { Name = "Business" };
        entitlement.Setup(service => service.GetCurrentAsync("actor", organizationId)).ReturnsAsync(subscription);
        entitlement.Setup(service => service.GetEffectivePlanAsync("actor", organizationId)).ReturnsAsync(plan);
        entitlement.Setup(service => service.GetMembersLimitAsync("actor", organizationId, Helpers.PlatformDefaults.WorkspaceMembersLimit)).ReturnsAsync(23);
        var vehicles = new Mock<IVehicleQueryService>();
        vehicles.Setup(service => service.GetVehiclesCountAsync("actor", organizationId)).ReturnsAsync(11);
        vehicles.Setup(service => service.GetArchivedVehiclesCountAsync("actor", organizationId)).ReturnsAsync(2);
        var limits = new Mock<IVehicleLimitQueryService>();
        limits.Setup(service => service.GetVehicleLimitInfoAsync("actor", organizationId))
            .ReturnsAsync(new VehicleLimitInfoDto { MaxVehiclesForUser = 29 });
        var service = new WorkspaceOverviewService(organizations.Object, access.Object,
            queries.Object, clients.Object, entitlement.Object, vehicles.Object, limits.Object, clock.Object);

        var result = await service.GetAsync("actor", organizationId);

        var overview = Assert.IsType<OrganizationWorkspaceOverviewDto>(result.Overview);
        Assert.Same(organization, overview.Organization);
        Assert.Same(allowed.WorkspaceAccess, overview.WorkspaceAccess);
        Assert.Same(allowed.FeatureSnapshot.Features, overview.WorkspaceFeatures);
        Assert.Same(subscription, overview.CurrentSubscription);
        Assert.Same(plan, overview.EffectivePlan);
        Assert.Equal(canViewVehicles ? 29 : 0, overview.WorkspaceVehicleLimit);
        Assert.Equal(23, overview.WorkspaceMembersLimit);
        Assert.Equal(canViewVehicles ? 11 : 0, overview.ActiveVehiclesCount);
        Assert.Equal(canViewVehicles ? 2 : 0, overview.ArchivedVehiclesCount);
        Assert.Equal(canViewClients ? 19 : 0, overview.ClientsCount);
        Assert.Equal(canViewAnalytics ? 7 : 0, overview.Planning.OverdueCount);
        Assert.Equal(canViewAnalytics ? 13 : 0, overview.Metrics.MaintenanceTotalCount);
        if (canViewAnalytics)
        {
            Assert.Same(planning, overview.Planning);
            Assert.Same(metrics, overview.Metrics);
        }
        queries.Verify(service => service.GetPlanningSummaryAsync(organizationId, true, true, true, today, It.Is<TimeZoneInfo>(zone => zone.Id == "Asia/Novosibirsk")),
            canViewAnalytics ? Times.Once() : Times.Never());
        queries.Verify(service => service.GetOverviewMetricsAsync(organizationId, true, true),
            canViewAnalytics ? Times.Once() : Times.Never());
        limits.Verify(service => service.GetVehicleLimitInfoAsync("actor", organizationId),
            canViewVehicles ? Times.Once() : Times.Never());
        clients.Verify(service => service.GetActiveCountAsync(organizationId), canViewClients ? Times.Once() : Times.Never());
        vehicles.Verify(service => service.GetVehiclesCountAsync("actor", organizationId), canViewVehicles ? Times.Once() : Times.Never());
        vehicles.Verify(service => service.GetArchivedVehiclesCountAsync("actor", organizationId), canViewVehicles ? Times.Once() : Times.Never());
    }
}
