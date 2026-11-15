using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Features;
using OpenCRM.Modules.Billing.Subscriptions;
using OpenCRM.Modules.Organizations;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos.Admin;
using OpenCRM.Testing;
using Moq;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection("Free PRO PostgreSQL")]
public sealed class FreeProTransitionTests(BillingDatabaseFixture fixture) : IClassFixture<BillingDatabaseFixture>
{
    [Theory]
    [InlineData(OrganizationTypeCodes.Detailing)]
    [InlineData(OrganizationTypeCodes.ServiceStation)]
    public async Task NewOrganizationHasWritableFreeWithQuotasAndNoAutomaticTrial(string scenario)
    {
        await using var context = fixture.CreateContext();
        await FreeProTransitionTests.SeedCommercialFreeQuotasAsync(context);
        var organization = await fixture.CreateOrganizationAsync(context, scenario: scenario);
        var permissions = new OrganizationPermissionService(context, Mock.Of<IOrganizationAuditService>());
        var access = OrganizationBillingTestServices.CreateAccess(context, permissions);
        var entitlements = OrganizationBillingTestServices.CreateEntitlement(context);
        var userId = organization.OwnerUserId;
        var workspace = await access.GetWorkspaceAccessAsync(userId, organization.Id);

        Assert.True(workspace.IsFreePlan);
        Assert.False(workspace.IsReadOnly);
        Assert.False(workspace.HasActiveSubscription);
        Assert.True(workspace.ExportsEnabled);
        Assert.Empty(await context.OrganizationSubscriptions.Where(item => item.OrganizationId == organization.Id).ToListAsync());
        Assert.Equal(300, await entitlements.GetVehicleLimitAsync(userId, organization.Id, 0));
        Assert.Equal(1, await entitlements.GetMembersLimitAsync(userId, organization.Id, 0));
        var features = await entitlements.GetFeatureSnapshotAsync(userId, organization.Id);
        Assert.Equal(300, features.Features.Single(item => item.Code == FeatureCodes.WorkspaceVehiclesLimit).IntValue);
        Assert.Equal(300, features.Features.Single(item => item.Code == FeatureCodes.WorkspaceClientsLimit).IntValue);
        Assert.False(await entitlements.HasFeatureAsync(userId, organization.Id, FeatureCodes.MaintenanceIntake));
        Assert.False(await entitlements.HasFeatureAsync(userId, organization.Id, FeatureCodes.Assistant));
        Assert.False(await entitlements.HasFeatureAsync(userId, organization.Id, FeatureCodes.ClientMessaging));
        Assert.False(await entitlements.HasFeatureAsync(userId, organization.Id, FeatureCodes.DocumentStyling));
        Assert.False(await entitlements.HasFeatureAsync(userId, organization.Id, FeatureCodes.DataImport));
        Assert.False(await entitlements.HasFeatureAsync(userId, organization.Id, FeatureCodes.FinanceExport1C));
        Assert.True(await entitlements.HasFeatureAsync(userId, organization.Id, FeatureCodes.MaterialsModule));
        Assert.True(await entitlements.HasFeatureAsync(userId, organization.Id, FeatureCodes.WikiModule));
        Assert.True(await entitlements.HasFeatureAsync(userId, organization.Id, FeatureCodes.ExportPdf));
        Assert.True(await entitlements.HasFeatureAsync(userId, organization.Id, FeatureCodes.ExportPdfWithPhotos));
        Assert.True(await entitlements.HasFeatureAsync(userId, organization.Id, FeatureCodes.ExportWord));
        Assert.True(await entitlements.HasFeatureAsync(userId, organization.Id, FeatureCodes.ExportWordWithPhotos));
        await access.EnsureWorkspaceCanWriteAsync(userId, organization.Id, OrganizationPermissionCodes.ClientsCreate);
        await access.EnsureWorkspaceCanWriteAsync(userId, organization.Id, OrganizationPermissionCodes.MaintenanceCreate);
        await access.EnsureWorkspaceCanWriteAsync(userId, organization.Id, OrganizationPermissionCodes.FinanceManage);
        await access.EnsureWorkspaceCanWriteAsync(userId, organization.Id, OrganizationPermissionCodes.MaterialsManage);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            access.EnsureWorkspaceCanWriteAsync(userId, organization.Id, OrganizationPermissionCodes.EmployeesManage));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            access.EnsureWorkspaceCanWriteAsync(userId, organization.Id, OrganizationPermissionCodes.ShiftsManage));
        var trial = await entitlements.CheckDemoAvailabilityAsync(userId, organization.Id);
        Assert.False(trial.Available);
        Assert.Equal("Пробный доступ не настроен: рабочее пространство работает без коммерческой подписки.", trial.Message);
    }

    [Fact]
    public async Task FinanceExportFeaturePreservesExplicitPlanRestriction()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, $"PRO-1C-{Guid.NewGuid():N}");
        await fixture.AddSubscriptionAsync(context, organization, plan, DateTime.UtcNow.AddDays(1));
        var entitlements = OrganizationBillingTestServices.CreateEntitlement(context);

        Assert.True(await entitlements.HasFeatureAsync(organization.OwnerUserId, organization.Id, FeatureCodes.FinanceExport1C));

        var assignment = await context.SubscriptionPlanFeatures.SingleAsync(item => item.SubscriptionPlanId == plan.Id
            && item.FeatureDefinition.Code == FeatureCodes.FinanceExport1C);
        assignment.BoolValue = false;
        await context.SaveChangesAsync();
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка опирается на тарифы организации: коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);

        Assert.False(await entitlements.HasFeatureAsync(organization.OwnerUserId, organization.Id, FeatureCodes.FinanceExport1C));
    }

    [Fact]
    public async Task ExpirationReturnsOwnerToFreeWithoutDeletingMembershipsOrSubscriptionHistory()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var employee = new ApplicationUser();
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            User = employee,
            RoleCode = OrganizationRoleCodes.Employee
        });

        var plan = await fixture.CreatePlanAsync(context, $"PRO-{Guid.NewGuid():N}");
        await fixture.AddSubscriptionAsync(context, organization, plan, DateTime.UtcNow.AddDays(1));
        var permissions = new OrganizationPermissionService(context, Mock.Of<IOrganizationAuditService>());
        var access = OrganizationBillingTestServices.CreateAccess(context, permissions);
        Assert.False((await access.GetWorkspaceAccessAsync(employee.Id, organization.Id)).IsReadOnly);
        var entitlements = OrganizationBillingTestServices.CreateEntitlement(context);
        Assert.True(await entitlements.HasFeatureAsync(organization.OwnerUserId, organization.Id, FeatureCodes.DocumentStyling));
        Assert.True(await entitlements.HasFeatureAsync(organization.OwnerUserId, organization.Id, FeatureCodes.DataImport));
        Assert.True(await entitlements.HasFeatureAsync(organization.OwnerUserId, organization.Id, FeatureCodes.FinanceExport1C));
        var subscription = await context.OrganizationSubscriptions.SingleAsync(item => item.OrganizationId == organization.Id);
        subscription.EndDate = DateTime.UtcNow.AddDays(-1);
        await context.SaveChangesAsync();

        var ownerAccess = await access.GetWorkspaceAccessAsync(organization.OwnerUserId, organization.Id);
        var employeeAccess = await access.GetWorkspaceAccessAsync(employee.Id, organization.Id);
        Assert.True(ownerAccess.IsFreePlan);
        Assert.False(ownerAccess.IsReadOnly);
        Assert.True(employeeAccess.IsReadOnly);
        Assert.False(await entitlements.HasFeatureAsync(organization.OwnerUserId, organization.Id, FeatureCodes.DocumentStyling));
        Assert.False(await entitlements.HasFeatureAsync(organization.OwnerUserId, organization.Id, FeatureCodes.DataImport));
        Assert.Equal(2, await context.OrganizationMembers.CountAsync(item => item.OrganizationId == organization.Id));
        Assert.Single(await context.OrganizationSubscriptions.Where(item => item.OrganizationId == organization.Id).ToListAsync());
        await access.EnsureWorkspaceCanReadAsync(employee.Id, organization.Id, OrganizationPermissionCodes.MaintenanceView);
    }

    [Fact]
    public async Task FreeInitializationPreservesOwnerSettingsAndLegacyDefaultAndCannotBePurchasedOrDeleted()
    {
        await using var context = fixture.CreateContext();
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка опирается на тарифы организации: коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var legacyPlan = await context.SubscriptionPlans.SingleAsync(item => item.IsDefaultPlan
            && item.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business);
        var legacyLimit = legacyPlan.VehicleLimit;
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка опирается на тарифы организации: коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var freePlan = await context.SubscriptionPlans.SingleAsync(item => item.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup);
        var materials = await context.SubscriptionPlanFeatures.SingleAsync(item => item.SubscriptionPlanId == freePlan.Id
            && item.FeatureDefinition.Code == FeatureCodes.MaterialsModule);
        var wiki = await context.SubscriptionPlanFeatures.SingleAsync(item => item.SubscriptionPlanId == freePlan.Id
            && item.FeatureDefinition.Code == FeatureCodes.WikiModule);
        var exportCodes = new[] { FeatureCodes.ExportPdf, FeatureCodes.ExportPdfWithPhotos, FeatureCodes.ExportWord, FeatureCodes.ExportWordWithPhotos };
        var exports = await context.SubscriptionPlanFeatures.Where(item => item.SubscriptionPlanId == freePlan.Id
            && exportCodes.Contains(item.FeatureDefinition.Code)).ToListAsync();
        var vehiclesLimit = await context.SubscriptionPlanFeatures.SingleAsync(item => item.SubscriptionPlanId == freePlan.Id
            && item.FeatureDefinition.Code == FeatureCodes.WorkspaceVehiclesLimit);
        var clientsLimit = await context.SubscriptionPlanFeatures.SingleAsync(item => item.SubscriptionPlanId == freePlan.Id
            && item.FeatureDefinition.Code == FeatureCodes.WorkspaceClientsLimit);

        materials.BoolValue = false;
        wiki.BoolValue = false;
        foreach (var export in exports)
        {
            export.BoolValue = false;
        }
        freePlan.Description = "До 1000 клиентов и 1000 автомобилей.";
        freePlan.VehicleLimit = 1000;
        vehiclesLimit.IntValue = 1000;
        clientsLimit.IntValue = 1000;
        await context.SaveChangesAsync();
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка опирается на тарифы организации: коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        Assert.False(materials.BoolValue);
        Assert.False(wiki.BoolValue);
        Assert.Equal(4, exports.Count);
        Assert.All(exports, export => Assert.False(export.BoolValue));
        Assert.Equal("До 1000 клиентов и 1000 автомобилей.", freePlan.Description);
        Assert.Equal(1000, freePlan.VehicleLimit);
        Assert.Equal(1000, vehiclesLimit.IntValue);
        Assert.Equal(1000, clientsLimit.IntValue);
        Assert.False(freePlan.IsDefaultPlan);
        Assert.True(legacyPlan.IsDefaultPlan);
        Assert.Equal(legacyLimit, legacyPlan.VehicleLimit);
        var organization = await fixture.CreateOrganizationAsync(context);
        var permissions = new OrganizationPermissionService(context, Mock.Of<IOrganizationAuditService>());
        var policy = OrganizationBillingTestServices.CreatePurchasePolicy(context, permissions);
        Assert.False((await policy.CanPurchasePlanAsync(organization.OwnerUserId, organization.Id, freePlan.Id)).Success);
        var catalog = OrganizationBillingTestServices.CreateCatalog(context, fixture.CreatePromoCodeService().Object);
        Assert.DoesNotContain(await catalog.GetAvailablePlansAsync(organization.OwnerUserId, organization.Id), item => item.Id == freePlan.Id);
        var admin = new SubscriptionPlanAdminService(context);
        var adminPlans = await admin.GetAllSubscriptionPlansAsync();
        Assert.False(adminPlans.Single(plan => plan.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup).CanDelete);
        Assert.False(await admin.DeleteSubscriptionPlanAsync(freePlan.Id));
        Assert.False(await admin.DeactivateSubscriptionPlanAsync(freePlan.Id));
        Assert.False(await admin.ToggleSubscriptionPlanStatusAsync(freePlan.Id));

        freePlan.VehicleLimit = 300;
        vehiclesLimit.IntValue = 300;
        clientsLimit.IntValue = 300;
        materials.BoolValue = true;
        wiki.BoolValue = true;
        foreach (var export in exports)
        {
            export.BoolValue = true;
        }
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task FreeLimitsSavedByAdminSurviveRepeatedInitializationAndApplyToOrganizations()
    {
        await using var context = fixture.CreateContext();
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка опирается на тарифы организации: коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var freePlan = await context.SubscriptionPlans.SingleAsync(item => item.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup);
        var admin = new SubscriptionPlanAdminService(context);
        var featuresAdmin = new FeatureAdminService(context);
        var definitions = await featuresAdmin.GetPlanFeaturesAsync(freePlan.Id);
        var vehicles = definitions.Single(item => item.Code == FeatureCodes.WorkspaceVehiclesLimit);
        var clients = definitions.Single(item => item.Code == FeatureCodes.WorkspaceClientsLimit);

        try
        {
            var updated = await admin.UpdateSubscriptionPlanAsync(freePlan.Id, new SubscriptionPlanUpdateDto
            {
                Name = freePlan.Name,
                Description = "Лимиты настроены администратором.",
                Price = 0,
                DurationDays = freePlan.DurationDays,
                VehicleLimit = 450,
                Group = freePlan.Group,
                PlanTypeCode = freePlan.PlanTypeCode,
                IsActive = true
            });

            Assert.NotNull(updated);
            Assert.Equal(450, (await featuresAdmin.GetPlanFeaturesAsync(freePlan.Id))
                .Single(item => item.Code == FeatureCodes.WorkspaceVehiclesLimit).IntValue);

            await featuresAdmin.SavePlanFeaturesAsync(freePlan.Id, new SavePlanFeaturesDto
            {
                Features =
                [
                    new() { FeatureId = vehicles.FeatureId, IsAssigned = true, IntValue = 650 },
                    new() { FeatureId = clients.FeatureId, IsAssigned = true, IntValue = 550 }
                ]
            });

            await FeatureBootstrapper.EnsureDefaultsAsync(context);
            // Проверка опирается на тарифы организации: коммерческий режим включается явно.
            await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
            await FeatureBootstrapper.EnsureDefaultsAsync(context);
            context.ChangeTracker.Clear();
            var persisted = await context.SubscriptionPlans.FindAsync(freePlan.Id);
            Assert.Equal(650, persisted!.VehicleLimit);
            Assert.Equal("Лимиты настроены администратором.", persisted.Description);
            var organization = await fixture.CreateOrganizationAsync(context);
            var entitlement = OrganizationBillingTestServices.CreateEntitlement(context);
            Assert.Equal(650, await entitlement.GetVehicleLimitAsync(organization.OwnerUserId, organization.Id, 0));
            var snapshot = await entitlement.GetFeatureSnapshotAsync(organization.OwnerUserId, organization.Id);
            Assert.Equal(550, snapshot.Features.Single(item => item.Code == FeatureCodes.WorkspaceClientsLimit).IntValue);

            await Assert.ThrowsAsync<InvalidOperationException>(() => featuresAdmin.SavePlanFeaturesAsync(freePlan.Id,
                new SavePlanFeaturesDto { Features = [new() { FeatureId = clients.FeatureId, IsAssigned = false }] }));
        }
        finally
        {
            context.ChangeTracker.Clear();
            await featuresAdmin.SavePlanFeaturesAsync(freePlan.Id, new SavePlanFeaturesDto
            {
                Features =
                [
                    new() { FeatureId = vehicles.FeatureId, IsAssigned = true, IntValue = 300 },
                    new() { FeatureId = clients.FeatureId, IsAssigned = true, IntValue = 300 }
                ]
            });
        }
    }

    [Fact]
    public async Task LegacyFleetWithoutSubscriptionRetainsItsOriginalAccessRules()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        var permissions = new OrganizationPermissionService(context, Mock.Of<IOrganizationAuditService>());
        var access = await OrganizationBillingTestServices.CreateAccess(context, permissions)
            .GetWorkspaceAccessAsync(organization.OwnerUserId, organization.Id);

        Assert.False(access.IsFreePlan);
        Assert.True(access.IsReadOnly);
    }

    private static async Task SeedCommercialFreeQuotasAsync(OpenCRMContext context)
    {
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var freePlan = await context.SubscriptionPlans.SingleAsync(
            plan => plan.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup);
        freePlan.VehicleLimit = 300;
        var vehicleLimit = await context.SubscriptionPlanFeatures.SingleAsync(feature =>
            feature.SubscriptionPlanId == freePlan.Id
            && feature.FeatureDefinition.Code == FeatureCodes.WorkspaceVehiclesLimit);
        var clientLimit = await context.SubscriptionPlanFeatures.SingleAsync(feature =>
            feature.SubscriptionPlanId == freePlan.Id
            && feature.FeatureDefinition.Code == FeatureCodes.WorkspaceClientsLimit);
        var memberLimit = await context.SubscriptionPlanFeatures.SingleAsync(feature =>
            feature.SubscriptionPlanId == freePlan.Id
            && feature.FeatureDefinition.Code == FeatureCodes.WorkspaceMembersLimit);
        vehicleLimit.IntValue = 300;
        clientLimit.IntValue = 300;
        memberLimit.IntValue = 1;
        await context.SaveChangesAsync();
    }
}
