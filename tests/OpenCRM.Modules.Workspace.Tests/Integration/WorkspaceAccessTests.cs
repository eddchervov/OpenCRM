using Helpers;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Billing.Contracts.Dtos;
using OpenCRM.Modules.Workspace.Access;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class WorkspaceAccessTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    /// <summary> Политика коммерческого режима для тестов тарифов: коммерция включена явно. </summary>
    private sealed class CommercialEnabledPolicy : IPlatformCommercialPolicy
    {
        /// <summary> Коммерческие тарифы включены. </summary>
        public bool CommercialEnabled => true;
    }

    [Theory]
    [InlineData(OrganizationRoleCodes.Owner, true, false)]
    [InlineData(OrganizationRoleCodes.Employee, true, false)]
    [InlineData(OrganizationRoleCodes.Viewer, true, true)]
    [InlineData(OrganizationRoleCodes.Employee, false, true)]
    public async Task NeutralWriteIgnoresExpiredPlanButPreservesViewerAndPermissionRestrictions(
        string role, bool permissionGranted, bool denied)
    {
        await using var context = fixture.CreateContext();
        var actor = new ApplicationUser();
        var organization = new Organization
        {
            Name = "Нейтральные права",
            OwnerUser = role == OrganizationRoleCodes.Owner ? actor : new ApplicationUser()
        };
        organization.Members.Add(new OrganizationMember { User = actor, RoleCode = role });
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var entitlement = new Mock<IOrganizationEntitlementService>();
        entitlement.Setup(service => service.GetWorkspaceEntitlementAsync(actor.Id, organization.Id))
            .ReturnsAsync(new OrganizationWorkspaceEntitlementDto { HasSubscriptionHistory = true, IsFreePlan = true });
        entitlement.Setup(service => service.HasFeatureAsync(actor.Id, organization.Id, It.IsAny<string>())).ReturnsAsync(true);
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(actor.Id, organization.Id, It.IsAny<string>())).ReturnsAsync(permissionGranted);
        var service = new WorkspaceAccessService(context, Mock.Of<IFeatureDefaultsInitializer>(), entitlement.Object, permissions.Object,
            Mock.Of<IPlatformCommercialPolicy>());

        var access = await service.GetWorkspaceAccessAsync(actor.Id, organization.Id);
        Assert.False(access.CommercialEnabled);

        if (denied)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.EnsureWorkspaceCanWriteAsync(actor.Id, organization.Id, OrganizationPermissionCodes.MembersInvite));
        }
        else
        {
            await service.EnsureWorkspaceCanWriteAsync(actor.Id, organization.Id, OrganizationPermissionCodes.MembersInvite);
        }

        entitlement.Verify(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()), denied ? Times.Never() : Times.Once());
    }

    [Theory]
    [InlineData("owner", true, false, false, true, true, true, false, null)]
    [InlineData("viewer", true, false, false, true, true, false, false, "только на просмотр")]
    [InlineData("owner", false, true, false, true, false, true, false, "Подключите новый тариф")]
    [InlineData("employee", false, true, false, true, false, false, false, "Срок тарифа закончился")]
    [InlineData("owner", false, false, false, true, false, true, true, "Можно подключить демо")]
    [InlineData("owner", false, false, false, true, false, true, false, "на демо или бизнес-тарифе")]
    [InlineData("employee", false, false, false, true, false, false, false, "не подключён тариф")]
    [InlineData("owner", false, false, true, true, true, true, false, null)]
    [InlineData("viewer", false, false, true, true, true, false, false, "только на просмотр")]
    [InlineData("employee", false, false, true, true, true, false, false, "Работа команды доступна в PRO")]
    [InlineData("employee", true, false, false, false, false, false, false, null)]
    public async Task AccessPreservesRoleAndSubscriptionMessages(
        string role,
        bool activeSubscription,
        bool history,
        bool freePlan,
        bool documentsExportPermission,
        bool expectedExportsEnabled,
        bool canManageBilling,
        bool demoAvailable,
        string? expectedMessage)
    {
        await using var context = fixture.CreateContext();
        var actor = new ApplicationUser();
        var organization = new Organization
        {
            Name = "Access matrix",
            OwnerUser = role == OrganizationRoleCodes.Owner ? actor : new ApplicationUser()
        };
        organization.Members.Add(new OrganizationMember { User = actor, RoleCode = role });
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var entitlement = new Mock<IOrganizationEntitlementService>();
        entitlement.Setup(service => service.GetWorkspaceEntitlementAsync(actor.Id, organization.Id))
            .ReturnsAsync(new OrganizationWorkspaceEntitlementDto
            {
                HasActiveSubscription = activeSubscription,
                HasSubscriptionHistory = history,
                IsBaseWorkspace = !activeSubscription,
                IsFreePlan = freePlan
            });
        entitlement.Setup(service => service.CheckDemoAvailabilityAsync(actor.Id, organization.Id))
            .ReturnsAsync((demoAvailable, "Demo eligibility"));
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(actor.Id, organization.Id, OrganizationPermissionCodes.BillingManage))
            .ReturnsAsync(canManageBilling);
        permissions.Setup(service => service.HasPermissionAsync(actor.Id, organization.Id, "allowed")).ReturnsAsync(true);
        permissions.Setup(service => service.HasPermissionAsync(
            actor.Id, organization.Id, OrganizationPermissionCodes.DocumentsExport)).ReturnsAsync(documentsExportPermission);
        var service = new WorkspaceAccessService(context, Mock.Of<IFeatureDefaultsInitializer>(), entitlement.Object, permissions.Object, new CommercialEnabledPolicy());

        var access = await service.GetWorkspaceAccessAsync(actor.Id, organization.Id);

        Assert.True(access.CommercialEnabled);
        Assert.Equal(role, access.RoleCode);
        Assert.Equal(activeSubscription, access.HasActiveSubscription);
        Assert.Equal(!activeSubscription, access.IsBaseWorkspace);
        Assert.Equal(role == OrganizationRoleCodes.Viewer
            || (freePlan && role != OrganizationRoleCodes.Owner)
            || (!activeSubscription && !freePlan), access.IsReadOnly);
        Assert.Equal(expectedExportsEnabled, access.ExportsEnabled);
        if (expectedMessage is null)
        {
            Assert.Null(access.ReadOnlyMessage);
        }
        else
        {
            Assert.Contains(expectedMessage, access.ReadOnlyMessage);
        }
        await service.EnsureWorkspaceCanReadAsync(actor.Id, organization.Id, "allowed");
    }

    [Theory]
    [InlineData("owner", true, true, true, true, true)]
    [InlineData("employee", true, true, true, false, false)]
    [InlineData("viewer", false, true, true, false, false)]
    [InlineData("employee", false, true, false, true, false)]
    [InlineData("employee", false, false, true, false, true)]
    [InlineData("employee", false, false, false, false, false)]
    public async Task VehicleWriteRightsRespectFreePlanAndIndependentProPermissions(
        string role,
        bool isFreePlan,
        bool canEditVehicles,
        bool canArchiveVehicles,
        bool expectedCanEditVehicles,
        bool expectedCanArchiveVehicles)
    {
        await using var context = fixture.CreateContext();
        var actor = new ApplicationUser();
        var organization = new Organization
        {
            Name = "Vehicle access matrix",
            OwnerUser = role == OrganizationRoleCodes.Owner ? actor : new ApplicationUser()
        };
        organization.Members.Add(new OrganizationMember { User = actor, RoleCode = role });
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var entitlement = new Mock<IOrganizationEntitlementService>();
        entitlement.Setup(service => service.GetWorkspaceEntitlementAsync(actor.Id, organization.Id))
            .ReturnsAsync(new OrganizationWorkspaceEntitlementDto
            {
                HasActiveSubscription = !isFreePlan,
                IsFreePlan = isFreePlan
            });
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(
                actor.Id, organization.Id, OrganizationPermissionCodes.VehiclesEdit))
            .ReturnsAsync(canEditVehicles);
        permissions.Setup(service => service.HasPermissionAsync(
                actor.Id, organization.Id, OrganizationPermissionCodes.VehiclesArchive))
            .ReturnsAsync(canArchiveVehicles);
        var service = new WorkspaceAccessService(context, Mock.Of<IFeatureDefaultsInitializer>(), entitlement.Object, permissions.Object, new CommercialEnabledPolicy());

        var access = await service.GetWorkspaceAccessAsync(actor.Id, organization.Id);

        Assert.Equal(expectedCanEditVehicles, access.CanEditVehicles);
        Assert.Equal(expectedCanArchiveVehicles, access.CanArchiveVehicles);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("foreign")]
    [InlineData("deleted")]
    [InlineData("archived")]
    [InlineData("inactive")]
    public async Task UnavailableOrganizationRejectsMembershipAndAccess(string state)
    {
        await using var context = fixture.CreateContext();
        var actor = new ApplicationUser();
        var organization = new Organization { Name = "Unavailable", OwnerUser = actor };
        if (state != "foreign")
        {
            organization.Members.Add(new OrganizationMember { User = actor, RoleCode = OrganizationRoleCodes.Owner });
        }
        organization.IsActive = state != "inactive";
        if (state == "deleted")
        {
            organization.Delete(DateTime.UtcNow);
        }
        if (state == "archived")
        {
            organization.Archive(DateTime.UtcNow);
        }
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var organizationId = state == "absent" ? Guid.NewGuid() : organization.Id;
        var entitlement = new Mock<IOrganizationEntitlementService>();
        entitlement.Setup(service => service.GetWorkspaceEntitlementAsync(actor.Id, organizationId))
            .ReturnsAsync(new OrganizationWorkspaceEntitlementDto());
        var service = new WorkspaceAccessService(context, Mock.Of<IFeatureDefaultsInitializer>(), entitlement.Object,
            Mock.Of<IOrganizationPermissionService>(MockBehavior.Strict), new CommercialEnabledPolicy());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetWorkspaceAccessAsync(actor.Id, organizationId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureWorkspaceCanReadAsync(actor.Id, organizationId, "read"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverviewPermissionsRequireEnabledModulesAndWriteChecksDoNotBypassReadOnly(bool modulesEnabled)
    {
        await using var context = fixture.CreateContext();
        var actor = new ApplicationUser();
        var organization = new Organization { Name = "Feature gate", OwnerUser = actor };
        organization.Members.Add(new OrganizationMember { User = actor, RoleCode = OrganizationRoleCodes.Owner });
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var entitlement = new Mock<IOrganizationEntitlementService>();
        entitlement.Setup(service => service.GetWorkspaceEntitlementAsync(actor.Id, organization.Id))
            .ReturnsAsync(new OrganizationWorkspaceEntitlementDto { HasActiveSubscription = true });
        entitlement.Setup(service => service.GetFeatureSnapshotAsync(actor.Id, organization.Id)).ReturnsAsync(new FeatureAccessSnapshotDto
        {
            Features =
            [
                new() { Code = FeatureCodes.VehiclesModule, BoolValue = modulesEnabled },
                new() { Code = FeatureCodes.ClientsModule, BoolValue = modulesEnabled },
                new() { Code = FeatureCodes.DefectsModule, BoolValue = modulesEnabled },
                new() { Code = FeatureCodes.CountersModule, BoolValue = modulesEnabled },
                new() { Code = FeatureCodes.SchedulerModule, BoolValue = modulesEnabled },
                new() { Code = "unrelated", BoolValue = null }
            ]
        });
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(actor.Id, organization.Id, It.IsAny<string>())).ReturnsAsync(true);
        var defaults = new Mock<IFeatureDefaultsInitializer>();
        var service = new WorkspaceAccessService(context, defaults.Object, entitlement.Object, permissions.Object, new CommercialEnabledPolicy());

        var overview = await service.GetOverviewAsync(actor.Id, organization.Id);

        Assert.Equal(modulesEnabled, overview.CanViewVehicles);
        Assert.Equal(modulesEnabled, overview.CanViewClients);
        Assert.Equal(modulesEnabled, overview.HasDefectsModule);
        Assert.Equal(modulesEnabled, overview.HasCountersModule);
        Assert.Equal(modulesEnabled, overview.HasSchedulerModule);
        Assert.True(overview.CanViewAnalytics);
        await service.EnsureWorkspaceCanReadAsync(actor.Id, organization.Id, OrganizationPermissionCodes.WorkspaceView);
        defaults.Verify(initializer => initializer.EnsureDefaultsAsync(), Times.Once());
        await service.EnsureWorkspaceCanWriteAsync(actor.Id, organization.Id);
        await service.EnsureWorkspaceCanWriteAsync(actor.Id, organization.Id, "custom");
        permissions.Setup(value => value.HasPermissionAsync(actor.Id, organization.Id, "denied")).ReturnsAsync(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureWorkspaceCanReadAsync(actor.Id, organization.Id, "denied"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureWorkspaceCanWriteAsync(actor.Id, organization.Id, "denied"));
        entitlement.Setup(value => value.GetWorkspaceEntitlementAsync(actor.Id, organization.Id))
            .ReturnsAsync(new OrganizationWorkspaceEntitlementDto());
        entitlement.Setup(value => value.CheckDemoAvailabilityAsync(actor.Id, organization.Id)).ReturnsAsync((false, "Unavailable"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureWorkspaceCanWriteAsync(actor.Id, organization.Id));
    }
}
