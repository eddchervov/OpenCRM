using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Billing.Features;
using OpenCRM.Modules.Organizations;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Testing;
using Moq;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection("Billing workspace access PostgreSQL")]
public sealed class WorkspaceAccessAcceptanceTests(BillingDatabaseFixture fixture) : IClassFixture<BillingDatabaseFixture>
{
    [Theory]
    [InlineData(OrganizationRoleCodes.Owner, 0, false, null)]
    [InlineData(OrganizationRoleCodes.Admin, 0, true, "Бесплатный тариф позволяет изменять данные владельцу организации. Работа команды доступна в PRO.")]
    [InlineData(OrganizationRoleCodes.Employee, 0, true, "Бесплатный тариф позволяет изменять данные владельцу организации. Работа команды доступна в PRO.")]
    [InlineData(OrganizationRoleCodes.Performer, 0, true, "Бесплатный тариф позволяет изменять данные владельцу организации. Работа команды доступна в PRO.")]
    [InlineData(OrganizationRoleCodes.Viewer, 0, true, "В этой организации у вас доступ только на просмотр.")]
    [InlineData(OrganizationRoleCodes.Owner, -1, false, null)]
    [InlineData(OrganizationRoleCodes.Admin, -1, true, "Бесплатный тариф позволяет изменять данные владельцу организации. Работа команды доступна в PRO.")]
    [InlineData(OrganizationRoleCodes.Employee, -1, true, "Бесплатный тариф позволяет изменять данные владельцу организации. Работа команды доступна в PRO.")]
    [InlineData(OrganizationRoleCodes.Performer, -1, true, "Бесплатный тариф позволяет изменять данные владельцу организации. Работа команды доступна в PRO.")]
    [InlineData(OrganizationRoleCodes.Viewer, -1, true, "В этой организации у вас доступ только на просмотр.")]
    [InlineData(OrganizationRoleCodes.Owner, 1, false, null)]
    [InlineData(OrganizationRoleCodes.Admin, 1, false, null)]
    [InlineData(OrganizationRoleCodes.Employee, 1, false, null)]
    [InlineData(OrganizationRoleCodes.Performer, 1, false, null)]
    [InlineData(OrganizationRoleCodes.Viewer, 1, true, "В этой организации у вас доступ только на просмотр.")]
    public async Task RoleAndSubscriptionApplyFreeAndPaidWriteRules(
        string roleCode, int expiryDays, bool isReadOnly, string? expectedMessage)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var actorId = organization.OwnerUserId;
        if (roleCode != OrganizationRoleCodes.Owner)
        {
            var actor = new ApplicationUser();
            context.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = organization.Id, User = actor, RoleCode = roleCode
            });
            await context.SaveChangesAsync();
            actorId = actor.Id;
        }

        if (expiryDays != 0)
        {
            var plan = await fixture.CreatePlanAsync(context, $"Доступ-{Guid.NewGuid():N}");
            await fixture.AddSubscriptionAsync(context, organization, plan, DateTime.UtcNow.AddDays(expiryDays));
        }

        var permissionService = new OrganizationPermissionService(context, Mock.Of<IOrganizationAuditService>());
        var accessService = OrganizationBillingTestServices.CreateAccess(context, permissionService);
        var access = await accessService.GetWorkspaceAccessAsync(actorId, organization.Id);
        var hasExportPermission = await permissionService.HasPermissionAsync(
            actorId, organization.Id, OrganizationPermissionCodes.DocumentsExport);
        Assert.Equal(roleCode, access.RoleCode);
        Assert.Equal(isReadOnly, access.IsReadOnly);
        Assert.Equal(OrganizationPermissionCatalog.RoleHasPermission(roleCode, OrganizationPermissionCodes.DocumentsExport), hasExportPermission);
        Assert.Equal((access.HasActiveSubscription || access.IsFreePlan) && hasExportPermission, access.ExportsEnabled);
        Assert.Equal(expiryDays > 0, access.HasActiveSubscription);
        Assert.Equal(expiryDays <= 0, access.IsFreePlan);
        Assert.Equal(expectedMessage, access.ReadOnlyMessage);
        await accessService.EnsureWorkspaceCanReadAsync(actorId, organization.Id, OrganizationPermissionCodes.WorkspaceView);
        if (isReadOnly)
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                accessService.EnsureWorkspaceCanWriteAsync(actorId, organization.Id));
            Assert.Equal(expectedMessage, exception.Message);
        }
        else
        {
            await accessService.EnsureWorkspaceCanWriteAsync(actorId, organization.Id);
            if (await permissionService.HasPermissionAsync(actorId, organization.Id, OrganizationPermissionCodes.MaintenanceEdit))
            {
                await accessService.EnsureWorkspaceCanWriteAsync(actorId, organization.Id, OrganizationPermissionCodes.MaintenanceEdit);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    accessService.EnsureWorkspaceCanWriteAsync(actorId, organization.Id, OrganizationPermissionCodes.MaintenanceEdit));
            }
        }
    }

    [Fact]
    public async Task PermissionOverridesAreReReadAndNoSubscriptionIsNeededForReading()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var employee = new ApplicationUser();
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id, User = employee, RoleCode = OrganizationRoleCodes.Employee
        });
        var permissionOverride = new OrganizationRolePermissionOverride
        {
            OrganizationId = organization.Id, RoleCode = OrganizationRoleCodes.Employee,
            PermissionCode = OrganizationPermissionCodes.AnalyticsView, IsEnabled = false,
            UpdatedByUserId = organization.OwnerUserId
        };
        var exportPermissionOverride = new OrganizationRolePermissionOverride
        {
            OrganizationId = organization.Id, RoleCode = OrganizationRoleCodes.Employee,
            PermissionCode = OrganizationPermissionCodes.DocumentsExport, IsEnabled = false,
            UpdatedByUserId = organization.OwnerUserId
        };
        context.OrganizationRolePermissionOverrides.Add(permissionOverride);
        context.OrganizationRolePermissionOverrides.Add(exportPermissionOverride);
        await context.SaveChangesAsync();
        var permissions = new OrganizationPermissionService(context, Mock.Of<IOrganizationAuditService>());
        var service = OrganizationBillingTestServices.CreateAccess(context, permissions);
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnsureWorkspaceCanReadAsync(employee.Id, organization.Id, OrganizationPermissionCodes.AnalyticsView));
        Assert.Equal(OrganizationPermissionCatalog.BuildDeniedMessage(OrganizationPermissionCodes.AnalyticsView), denied.Message);
        Assert.False((await service.GetWorkspaceAccessAsync(employee.Id, organization.Id)).ExportsEnabled);
        permissionOverride.IsEnabled = true;
        exportPermissionOverride.IsEnabled = true;
        await context.SaveChangesAsync();
        await service.EnsureWorkspaceCanReadAsync(employee.Id, organization.Id, OrganizationPermissionCodes.AnalyticsView);
        Assert.True((await service.GetWorkspaceAccessAsync(employee.Id, organization.Id)).ExportsEnabled);
        Assert.True((await service.GetOverviewAsync(employee.Id, organization.Id)).CanViewAnalytics);
        var plan = await fixture.CreatePlanAsync(context, $"Отзыв-{Guid.NewGuid():N}");
        await fixture.AddSubscriptionAsync(context, organization, plan, DateTime.UtcNow.AddDays(2));
        await service.EnsureWorkspaceCanWriteAsync(employee.Id, organization.Id, OrganizationPermissionCodes.MaintenanceEdit);
        var subscription = await context.OrganizationSubscriptions.SingleAsync(item => item.OrganizationId == organization.Id);
        subscription.EndDate = DateTime.UtcNow.AddDays(-1);
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnsureWorkspaceCanWriteAsync(employee.Id, organization.Id, OrganizationPermissionCodes.MaintenanceEdit));
    }

    [Fact]
    public async Task PaidWorkspaceStillRequiresPermissionAndHistoricalFirstWorkspaceBlocksDemoHint()
    {
        await using var context = fixture.CreateContext();
        var first = await fixture.CreateOrganizationAsync(context, createdAt: DateTime.UtcNow.AddDays(-10));
        first.IsDeleted = true;
        await context.SaveChangesAsync();
        var organization = await fixture.CreateOrganizationAsync(context, first.OwnerUserId);
        var permissions = new OrganizationPermissionService(context, Mock.Of<IOrganizationAuditService>());
        var service = OrganizationBillingTestServices.CreateAccess(context, permissions);
        var access = await service.GetWorkspaceAccessAsync(organization.OwnerUserId, organization.Id);
        Assert.DoesNotContain("Можно подключить демо", access.ReadOnlyMessage);
        Assert.True(access.IsBaseWorkspace);
        var employee = new ApplicationUser();
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id, User = employee, RoleCode = OrganizationRoleCodes.Employee
        });
        context.OrganizationRolePermissionOverrides.Add(new OrganizationRolePermissionOverride
        {
            OrganizationId = organization.Id, RoleCode = OrganizationRoleCodes.Employee,
            PermissionCode = OrganizationPermissionCodes.MaintenanceEdit, IsEnabled = false,
            UpdatedByUserId = organization.OwnerUserId
        });
        var plan = await fixture.CreatePlanAsync(context, $"Запрет-{Guid.NewGuid():N}");
        await fixture.AddSubscriptionAsync(context, organization, plan, DateTime.UtcNow.AddDays(1));
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnsureWorkspaceCanWriteAsync(employee.Id, organization.Id, OrganizationPermissionCodes.MaintenanceEdit));
        Assert.Equal(OrganizationPermissionCatalog.BuildDeniedMessage(OrganizationPermissionCodes.MaintenanceEdit), denied.Message);
    }

    [Theory]
    [InlineData(OrganizationTypeCodes.Detailing, false)]
    [InlineData(OrganizationTypeCodes.Detailing, true)]
    [InlineData(OrganizationTypeCodes.Fleet, true)]
    public async Task MembersWriteRequiresConfiguredFeatureForPaidPlan(string scenario, bool membersModuleEnabled)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: scenario);
        var plan = await fixture.CreatePlanAsync(context, $"PRO-участники-{Guid.NewGuid():N}", isDefault: true);
        await fixture.AddSubscriptionAsync(context, organization, plan, DateTime.UtcNow.AddDays(1));
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка опирается на тарифы организации: коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var membersFeature = await context.SubscriptionPlanFeatures.SingleAsync(feature =>
            feature.SubscriptionPlanId == plan.Id && feature.FeatureDefinition.Code == FeatureCodes.MembersModule);
        membersFeature.BoolValue = membersModuleEnabled;
        await context.SaveChangesAsync();

        var permissions = new OrganizationPermissionService(context, Mock.Of<IOrganizationAuditService>());
        var service = OrganizationBillingTestServices.CreateAccess(context, permissions);
        if (membersModuleEnabled)
        {
            await service.EnsureWorkspaceCanWriteAsync(
                organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.EmployeesManage);
            return;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnsureWorkspaceCanWriteAsync(
                organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.EmployeesManage));
        Assert.Equal("Эта возможность доступна в PRO.", exception.Message);
    }

    [Theory]
    [InlineData(OrganizationRoleCodes.Owner, false)]
    [InlineData(OrganizationRoleCodes.Owner, true)]
    [InlineData(OrganizationRoleCodes.Admin, false)]
    [InlineData(OrganizationRoleCodes.Admin, true)]
    [InlineData(OrganizationRoleCodes.Employee, false)]
    [InlineData(OrganizationRoleCodes.Employee, true)]
    [InlineData(OrganizationRoleCodes.Performer, false)]
    [InlineData(OrganizationRoleCodes.Performer, true)]
    [InlineData(OrganizationRoleCodes.Viewer, false)]
    [InlineData(OrganizationRoleCodes.Viewer, true)]
    [InlineData("outsider", false)]
    [InlineData("outsider", true)]
    public async Task TeamMutationsRequireBothProAndTheSpecificRolePermission(string roleCode, bool hasPro)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var actorId = organization.OwnerUserId;
        if (roleCode != OrganizationRoleCodes.Owner)
        {
            var actor = new ApplicationUser();
            context.ApplicationUsers.Add(actor);
            if (roleCode != "outsider")
            {
                context.OrganizationMembers.Add(new OrganizationMember
                {
                    OrganizationId = organization.Id, User = actor, RoleCode = roleCode
                });
            }
            await context.SaveChangesAsync();
            actorId = actor.Id;
        }
        if (hasPro)
        {
            var plan = await fixture.CreatePlanAsync(context, $"Матрица-команды-{Guid.NewGuid():N}", isDefault: true);
            await fixture.AddSubscriptionAsync(context, organization, plan, DateTime.UtcNow.AddDays(1));
        }

        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка опирается на тарифы организации: коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var permissions = new OrganizationPermissionService(context, Mock.Of<IOrganizationAuditService>());
        var service = OrganizationBillingTestServices.CreateAccess(context, permissions);
        var permissionCodes = new[]
        {
            OrganizationPermissionCodes.EmployeesManage,
            OrganizationPermissionCodes.MembersInvite,
            OrganizationPermissionCodes.MembersRolesManage,
            OrganizationPermissionCodes.ShiftsManage,
            OrganizationPermissionCodes.MaintenanceWorkExecute
        };

        foreach (var permissionCode in permissionCodes)
        {
            var canWrite = hasPro && (roleCode is OrganizationRoleCodes.Owner or OrganizationRoleCodes.Admin
                || (roleCode == OrganizationRoleCodes.Performer && permissionCode == OrganizationPermissionCodes.MaintenanceWorkExecute));
            if (canWrite)
            {
                await service.EnsureWorkspaceCanWriteAsync(actorId, organization.Id, permissionCode);
                continue;
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.EnsureWorkspaceCanWriteAsync(actorId, organization.Id, permissionCode));
        }
    }

    [Fact]
    public async Task FreeOwnerCanRevokeExistingMemberAccessWithoutPurchasingPro()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var permissions = new OrganizationPermissionService(context, Mock.Of<IOrganizationAuditService>());
        var service = OrganizationBillingTestServices.CreateAccess(context, permissions);

        await service.EnsureWorkspaceCanWriteAsync(
            organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.MembersRemove);
    }

    [Fact]
    public async Task SubscriptionIncludesExactExpiryInstantAndExcludesImmediatelyEarlierEndDate()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, $"Граница-{Guid.NewGuid():N}");
        await fixture.AddSubscriptionAsync(context, organization, plan, DateTime.UtcNow.AddDays(1));
        var entitlement = OrganizationBillingTestServices.CreateEntitlement(context);
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();

            // В PostgreSQL now() фиксируется на начало транзакции, поэтому граница проверяется без задержек и подмены часов.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "OrganizationSubscriptions"
                SET "EndDate" = transaction_timestamp()
                WHERE "OrganizationId" = {organization.Id}
                """);

            Assert.True(await entitlement.HasActiveSubscriptionAsync(organization.OwnerUserId, organization.Id));
            Assert.NotNull(await entitlement.GetCurrentAsync(organization.OwnerUserId, organization.Id));

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "OrganizationSubscriptions"
                SET "EndDate" = transaction_timestamp() - interval '1 microsecond'
                WHERE "OrganizationId" = {organization.Id}
                """);

            Assert.False(await entitlement.HasActiveSubscriptionAsync(organization.OwnerUserId, organization.Id));
            Assert.Null(await entitlement.GetCurrentAsync(organization.OwnerUserId, organization.Id));

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "OrganizationSubscriptions"
                SET "EndDate" = transaction_timestamp() + interval '1 microsecond'
                WHERE "OrganizationId" = {organization.Id}
                """);

            Assert.True(await entitlement.HasActiveSubscriptionAsync(organization.OwnerUserId, organization.Id));
            Assert.NotNull(await entitlement.GetCurrentAsync(organization.OwnerUserId, organization.Id));
            await transaction.RollbackAsync();
        });
    }
}
