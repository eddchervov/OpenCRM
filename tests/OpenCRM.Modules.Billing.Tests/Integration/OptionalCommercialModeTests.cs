using Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Billing.Features;
using OpenCRM.Services.Interfaces.Features;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class OptionalCommercialModeTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalPlansAndGrantsOnlyRestrictCapabilitiesWhenCommercialModeIsEnabled(bool commercialEnabled)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var feature = await fixture.CreateFeatureAsync(context, FeatureCodes.ExportWord, "Экспорт Word", FeatureValueType.Boolean,
            FeatureCatalogCodes.Scopes.Both);
        var personalLimit = await fixture.CreateFeatureAsync(context, FeatureCodes.VehiclesLimit, "Личная техника", FeatureValueType.Integer,
            FeatureCatalogCodes.Scopes.Personal);
        var organizationLimit = await fixture.CreateFeatureAsync(context, FeatureCodes.WorkspaceVehiclesLimit, "Техника организации", FeatureValueType.Integer);
        var billing = await fixture.CreateFeatureAsync(context, FeatureCodes.BillingModule, "Подписки", FeatureValueType.Boolean,
            FeatureCatalogCodes.Scopes.Both);
        var unknown = await fixture.CreateFeatureAsync(context, "unknown.custom.feature", "Неизвестная возможность", FeatureValueType.Boolean,
            FeatureCatalogCodes.Scopes.Both);
        var plan = await fixture.CreatePlanAsync(context, "Исторический ограничивающий тариф", vehicleLimit: 2,
            features: [(feature, false, null), (personalLimit, null, 2), (organizationLimit, null, 2), (billing, true, null), (unknown, true, null)]);
        await fixture.AddSubscriptionAsync(context, organization, plan, DateTime.UtcNow.AddDays(10));
        context.UserSubscriptions.Add(new UserSubscription
        {
            UserId = organization.OwnerUserId,
            SubscriptionPlanId = plan.Id,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10)
        });
        context.UserFeatureGrants.Add(new UserFeatureGrant
        {
            UserId = organization.OwnerUserId,
            FeatureDefinitionId = feature.Id,
            BoolValue = false
        });
        await context.SaveChangesAsync();
        var policy = Mock.Of<IPlatformCommercialPolicy>(value => value.CommercialEnabled == commercialEnabled);
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object, platformCommercialPolicy: policy);
        using var scope = provider.CreateScope();
        var personal = scope.ServiceProvider.GetRequiredService<IFeatureAccessService>();
        var organizational = scope.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();

        Assert.Equal(!commercialEnabled, await personal.HasFeatureAsync(organization.OwnerUserId, feature.Code));
        Assert.Equal(!commercialEnabled, await organizational.HasFeatureAsync(organization.OwnerUserId, organization.Id, feature.Code));
        Assert.Equal(commercialEnabled ? 2 : 0, await personal.GetIntValueAsync(organization.OwnerUserId, personalLimit.Code, 99));
        Assert.Equal(commercialEnabled ? 2 : 0, await organizational.GetVehicleLimitAsync(organization.OwnerUserId, organization.Id, 99));
        Assert.Equal(commercialEnabled, await personal.HasFeatureAsync(organization.OwnerUserId, billing.Code));
        Assert.Equal(commercialEnabled, await organizational.HasFeatureAsync(organization.OwnerUserId, organization.Id, billing.Code));
        Assert.Equal(commercialEnabled, await organizational.HasActiveSubscriptionAsync(organization.OwnerUserId, organization.Id));
        if (commercialEnabled)
        {
            Assert.True(await personal.HasFeatureAsync(organization.OwnerUserId, unknown.Code));
            Assert.NotNull(await organizational.GetEffectivePlanAsync(organization.OwnerUserId, organization.Id));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => personal.HasFeatureAsync(organization.OwnerUserId, unknown.Code));
            Assert.False(await organizational.HasFeatureAsync(organization.OwnerUserId, organization.Id, unknown.Code));
            Assert.DoesNotContain((await personal.GetSnapshotAsync(organization.OwnerUserId)).Features, item => item.Code == unknown.Code);
            Assert.Null(await organizational.GetCurrentAsync(organization.OwnerUserId, organization.Id));
            Assert.Null(await organizational.GetEffectivePlanAsync(organization.OwnerUserId, organization.Id));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => organizational.HasFeatureAsync("outsider", organization.Id, feature.Code));
        Assert.Equal(1, await context.OrganizationSubscriptions.CountAsync());
        Assert.Equal(1, await context.UserSubscriptions.CountAsync());
        Assert.False((await context.SubscriptionPlanFeatures.SingleAsync(item => item.FeatureDefinitionId == feature.Id)).BoolValue);

        feature.IsActive = false;
        await context.SaveChangesAsync();

        Assert.False(await organizational.HasFeatureAsync(organization.OwnerUserId, organization.Id, feature.Code));
        await Assert.ThrowsAsync<InvalidOperationException>(() => personal.HasFeatureAsync(organization.OwnerUserId, feature.Code));
    }

    [Fact]
    public async Task NeutralBootstrapDoesNotChangeHistoricalPlanAssignments()
    {
        await using var context = fixture.CreateContext();
        var legacy = await fixture.CreateFeatureAsync(context, "workspace.count.limit", "Прежний лимит", FeatureValueType.Integer);
        var plan = await fixture.CreatePlanAsync(context, "Исторический план", features: [(legacy, null, 7)]);
        var assignment = await context.SubscriptionPlanFeatures.SingleAsync();

        await OpenCRM.Modules.Billing.Features.FeatureBootstrapper.EnsureDefaultsAsync(context);

        context.ChangeTracker.Clear();
        var saved = await context.SubscriptionPlanFeatures.SingleAsync(item => item.Id == assignment.Id);
        Assert.Equal(plan.Id, saved.SubscriptionPlanId);
        Assert.Equal(7, saved.IntValue);
        Assert.True((await context.FeatureDefinitions.SingleAsync(item => item.Id == legacy.Id)).IsActive);
        Assert.Equal(1, await context.SubscriptionPlans.CountAsync());
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
        Assert.Empty(await context.OrganizationSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task NeutralPersonalQueriesDoNotInitializePlansOrSubscriptions()
    {
        await using var context = fixture.CreateContext();
        var defaults = fixture.CreateDefaultsInitializer();
        using var provider = fixture.CreateServices(context, defaults.Object, platformCommercialPolicy: Mock.Of<IPlatformCommercialPolicy>());
        using var scope = provider.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionQueryService>();

        await queries.EnsureDefaultPlanAccessAsync("local-user");

        Assert.Null(await queries.GetUserSubscriptionAsync("local-user"));
        Assert.Null(await queries.GetNextPlanAsync("local-user"));
        Assert.Empty(await queries.GetCombinedSubscriptionHistoryAsync("local-user"));
        Assert.False(await queries.HasActiveSubscriptionAsync("local-user"));
        Assert.False(await queries.IsMaxPlanAsync("local-user"));
        Assert.False((await queries.CheckDemoSubscriptionAvailabilityAsync("local-user")).Available);
        defaults.Verify(service => service.EnsureDefaultsAsync(), Times.Never);
        Assert.Empty(await context.SubscriptionPlans.ToListAsync());
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task CommercialBootstrapUsesInstallationQuotasAndPreservesExistingValues()
    {
        await using var context = fixture.CreateContext();
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        var defaults = new CommercialPlanDefaults
        {
            FreeVehicleLimit = 30,
            FreeClientLimit = 40,
            FreeMembersLimit = 2,
            WorkspaceMembersLimit = 5,
            PersonalVehicleLimit = 25,
            DemoVehicleLimit = 70
        };

        await FeatureBootstrapper.EnsureCommercialPlansAsync(context, defaults);

        var freePlan = await context.SubscriptionPlans.SingleAsync(plan =>
            plan.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup);
        var freeFeatures = await context.SubscriptionPlanFeatures
            .Include(feature => feature.FeatureDefinition)
            .Where(feature => feature.SubscriptionPlanId == freePlan.Id)
            .ToDictionaryAsync(feature => feature.FeatureDefinition.Code);
        var demoPlan = await context.SubscriptionPlans.SingleAsync(plan => plan.IsDemoPlan
            && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business);

        Assert.Equal(30, freePlan.VehicleLimit);
        Assert.Equal(30, freeFeatures[FeatureCodes.WorkspaceVehiclesLimit].IntValue);
        Assert.Equal(40, freeFeatures[FeatureCodes.WorkspaceClientsLimit].IntValue);
        Assert.Equal(2, freeFeatures[FeatureCodes.WorkspaceMembersLimit].IntValue);
        Assert.Equal(70, demoPlan.VehicleLimit);
        var commercialDefaultPlan = await context.SubscriptionPlans.SingleAsync(plan => plan.IsDefaultPlan
            && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business);
        var commercialFeatures = await context.SubscriptionPlanFeatures
            .Include(feature => feature.FeatureDefinition)
            .Where(feature => feature.SubscriptionPlanId == commercialDefaultPlan.Id)
            .ToDictionaryAsync(feature => feature.FeatureDefinition.Code);
        Assert.Equal(5, commercialFeatures[FeatureCodes.WorkspaceMembersLimit].IntValue);

        freePlan.VehicleLimit = 31;
        freePlan.Price = 12;
        freeFeatures[FeatureCodes.WorkspaceClientsLimit].IntValue = 41;
        await context.SaveChangesAsync();
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context, new CommercialPlanDefaults
        {
            FreeVehicleLimit = 300,
            FreeClientLimit = 300,
            FreeMembersLimit = 1,
            WorkspaceMembersLimit = 10,
            PersonalVehicleLimit = 25,
            DemoVehicleLimit = 100
        });

        context.ChangeTracker.Clear();
        freePlan = await context.SubscriptionPlans.SingleAsync(plan =>
            plan.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup);
        var savedClientLimit = await context.SubscriptionPlanFeatures
            .Include(feature => feature.FeatureDefinition)
            .SingleAsync(feature => feature.SubscriptionPlanId == freePlan.Id
                && feature.FeatureDefinition.Code == FeatureCodes.WorkspaceClientsLimit);
        demoPlan = await context.SubscriptionPlans.SingleAsync(plan => plan.IsDemoPlan
            && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business);

        Assert.Equal(31, freePlan.VehicleLimit);
        Assert.Equal(12m, freePlan.Price);
        Assert.Equal(41, savedClientLimit.IntValue);
        Assert.Equal(70, demoPlan.VehicleLimit);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 25)]
    public async Task PersonalVehicleFallbackUsesCommercialSettingOnlyWhenEnabled(bool commercialEnabled, int expectedLimit)
    {
        await using var context = fixture.CreateContext();
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        var defaultsInitializer = fixture.CreateDefaultsInitializer();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformPolicy:Commercial:Defaults:PersonalVehicleLimit"] = "25"
            })
            .Build();
        var policy = Mock.Of<IPlatformCommercialPolicy>(value => value.CommercialEnabled == commercialEnabled);
        var access = new FeatureAccessService(context, defaultsInitializer.Object, policy, configuration);

        Assert.Equal(expectedLimit, await access.GetIntValueAsync("local-user", FeatureCodes.VehiclesLimit));
    }

    [Fact]
    public void NegativeCommercialQuotaIsRejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformPolicy:Commercial:Defaults:FreeClientLimit"] = "-1"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CommercialPlanDefaults.FromConfiguration(configuration));

        Assert.Contains("FreeClientLimit", exception.Message);
    }
}
