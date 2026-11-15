using OpenCRM.Modules.Billing.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Organizations;
using OpenCRM.Modules.Workspace.Queries;
using OpenCRM.Modules.Workspace.Shell;
using OpenCRM.Modules.Workspace.Access;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class WorkspaceAuthorizationAcceptanceTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Fact]
    public async Task WorkspaceAccessUsesRealMembershipRoleSubscriptionAndOrganizationState()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        var employee = new ApplicationUser();
        var observer = new ApplicationUser();
        var foreignUser = new ApplicationUser();
        var activeOrganization = WorkspaceAuthorizationAcceptanceTests.CreateOrganization(owner, "Active workspace");
        activeOrganization.Members.Add(new OrganizationMember { User = employee, RoleCode = OrganizationRoleCodes.Employee });
        activeOrganization.Members.Add(new OrganizationMember { User = observer, RoleCode = OrganizationRoleCodes.Viewer });
        var expiredOwner = new ApplicationUser();
        var expiredOrganization = WorkspaceAuthorizationAcceptanceTests.CreateOrganization(expiredOwner, "Expired workspace");
        var archivedOwner = new ApplicationUser();
        var archivedOrganization = WorkspaceAuthorizationAcceptanceTests.CreateOrganization(archivedOwner, "Archived workspace");
        archivedOrganization.Archive(DateTime.UtcNow);
        var inactiveOwner = new ApplicationUser();
        var inactiveOrganization = WorkspaceAuthorizationAcceptanceTests.CreateOrganization(inactiveOwner, "Inactive workspace");
        inactiveOrganization.IsActive = false;
        var plan = new SubscriptionPlan
        {
            Name = "Acceptance business plan",
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
            DurationDays = 30,
            Price = 1000m,
            VehicleLimit = 20
        };
        context.Users.AddRange(owner, employee, observer, foreignUser, expiredOwner, archivedOwner, inactiveOwner);
        context.Organizations.AddRange(activeOrganization, expiredOrganization, archivedOrganization, inactiveOrganization);
        context.SubscriptionPlans.Add(plan);
        context.OrganizationSubscriptions.AddRange(
            WorkspaceAuthorizationAcceptanceTests.CreateSubscription(activeOrganization, plan, DateTime.UtcNow.AddDays(30)),
            WorkspaceAuthorizationAcceptanceTests.CreateSubscription(expiredOrganization, plan, DateTime.UtcNow.AddDays(-1)));
        context.OrganizationRolePermissionOverrides.Add(new OrganizationRolePermissionOverride
        {
            Organization = activeOrganization,
            RoleCode = OrganizationRoleCodes.Employee,
            PermissionCode = OrganizationPermissionCodes.AnalyticsView,
            IsEnabled = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            UpdatedByUserId = owner.Id
        });
        await context.SaveChangesAsync();

        var permissionService = new OrganizationPermissionService(context, Mock.Of<IOrganizationAuditService>());
        var subscriptionService = WorkspaceAuthorizationAcceptanceTests.CreateSubscriptionService(context, permissionService);
        var workspaceAccessService = OrganizationBillingTestServices.CreateAccess(context, permissionService);

        var ownerAccess = await subscriptionService.GetWorkspaceAccessAsync(owner.Id, activeOrganization.Id);
        Assert.Equal(OrganizationRoleCodes.Owner, ownerAccess.RoleCode);
        Assert.False(ownerAccess.IsReadOnly);

        var employeeAccess = await subscriptionService.GetWorkspaceAccessAsync(employee.Id, activeOrganization.Id);
        Assert.Equal(OrganizationRoleCodes.Employee, employeeAccess.RoleCode);
        Assert.False(employeeAccess.IsReadOnly);
        var employeeOverviewAccess = await workspaceAccessService.GetOverviewAsync(employee.Id, activeOrganization.Id);
        Assert.False(employeeOverviewAccess.CanViewAnalytics);
        Assert.True(await permissionService.HasPermissionAsync(employee.Id, activeOrganization.Id, OrganizationPermissionCodes.FinanceView));
        Assert.False(await permissionService.HasPermissionAsync(employee.Id, activeOrganization.Id, OrganizationPermissionCodes.AnalyticsView));
        await subscriptionService.EnsureWorkspaceCanReadAsync(employee.Id, activeOrganization.Id, OrganizationPermissionCodes.FinanceView);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            subscriptionService.EnsureWorkspaceCanReadAsync(employee.Id, activeOrganization.Id, OrganizationPermissionCodes.AnalyticsView));

        var observerAccess = await subscriptionService.GetWorkspaceAccessAsync(observer.Id, activeOrganization.Id);
        Assert.Equal(OrganizationRoleCodes.Viewer, observerAccess.RoleCode);
        Assert.True(observerAccess.IsReadOnly);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            permissionService.EnsurePermissionAsync(observer.Id, activeOrganization.Id, OrganizationPermissionCodes.MaintenanceEdit));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            subscriptionService.GetWorkspaceAccessAsync(foreignUser.Id, activeOrganization.Id));

        var expiredAccess = await subscriptionService.GetWorkspaceAccessAsync(expiredOwner.Id, expiredOrganization.Id);
        Assert.False(expiredAccess.IsReadOnly);
        Assert.True(expiredAccess.IsFreePlan);
        Assert.False(expiredAccess.HasActiveSubscription);
        await subscriptionService.EnsureWorkspaceCanWriteAsync(
            expiredOwner.Id, expiredOrganization.Id, OrganizationPermissionCodes.MaintenanceEdit);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            subscriptionService.GetWorkspaceAccessAsync(archivedOwner.Id, archivedOrganization.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            subscriptionService.GetWorkspaceAccessAsync(inactiveOwner.Id, inactiveOrganization.Id));
    }

    private static Organization CreateOrganization(ApplicationUser owner, string name)
    {
        return new Organization
        {
            Name = name,
            OwnerUser = owner,
            PrimaryScenarioCode = OrganizationTypeCodes.Detailing,
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
    }

    private static OrganizationSubscription CreateSubscription(Organization organization, SubscriptionPlan plan, DateTime endDate)
    {
        return new OrganizationSubscription
        {
            Organization = organization,
            SubscriptionPlan = plan,
            StartDate = DateTime.UtcNow.AddDays(-30),
            EndDate = endDate,
            SourceCode = "acceptance"
        };
    }

    private static OrganizationSubscriptionService CreateSubscriptionService(
        OpenCRMContext context,
        IOrganizationPermissionService permissionService)
    {
        return new OrganizationSubscriptionService(
            Mock.Of<IOrganizationSubscriptionCatalogQueryService>(),
            OrganizationBillingTestServices.CreateEntitlement(context),
            OrganizationBillingTestServices.CreateAccess(context, permissionService),
            Mock.Of<IOrganizationSubscriptionCheckoutService>(),
            OrganizationBillingTestServices.CreateExtensionQuery(context, permissionService),
            Mock.Of<IOrganizationSubscriptionCommandService>(),
            Mock.Of<IOrganizationSubscriptionActivationService>(),
            Mock.Of<IOrganizationSubscriptionPurchasePolicyService>());
    }
}
