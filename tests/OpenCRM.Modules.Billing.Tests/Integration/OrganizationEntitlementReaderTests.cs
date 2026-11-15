using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Helpers;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class OrganizationEntitlementReaderTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CurrentSubscriptionUsesActiveRowWithGreatestEndDateAndMapsPlan()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        var pastPlan = await fixture.CreatePlanAsync(context, $"Истекший-{Guid.NewGuid():N}");
        var firstPlan = await fixture.CreatePlanAsync(context, $"Первый-{Guid.NewGuid():N}");
        var latestPlan = await fixture.CreatePlanAsync(context, $"Последний-{Guid.NewGuid():N}");
        await fixture.AddSubscriptionAsync(context, organization, pastPlan, DateTime.UtcNow.AddDays(-1));
        await fixture.AddSubscriptionAsync(context, organization, firstPlan, DateTime.UtcNow.AddDays(3));
        await fixture.AddSubscriptionAsync(context, organization, latestPlan, DateTime.UtcNow.AddDays(10), DateTime.UtcNow.AddDays(3));
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();

        var current = await service.GetCurrentAsync(organization.OwnerUserId, organization.Id);

        Assert.NotNull(current);
        Assert.Equal(latestPlan.Id, current.SubscriptionPlanId);
        Assert.Equal(latestPlan.Name, current.SubscriptionPlan.Name);
        Assert.True(current.IsActive);
    }

    [Fact]
    public async Task CurrentAndEffectivePlanAreNullWhenNoSubscriptionExists()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();

        Assert.Null(await service.GetCurrentAsync(organization.OwnerUserId, organization.Id));
        Assert.Null(await service.GetEffectivePlanAsync(organization.OwnerUserId, organization.Id));
    }

    [Fact]
    public async Task ActiveSubscriptionUsesEndDateRegardlessOfStartDateAndIsScopedToOrganization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        var otherOrganization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        var plan = await fixture.CreatePlanAsync(context, $"Тариф-{Guid.NewGuid():N}");
        var futureStart = DateTime.UtcNow.AddDays(5);
        await fixture.AddSubscriptionAsync(context, organization, plan, DateTime.UtcNow.AddDays(10), futureStart);
        await fixture.AddSubscriptionAsync(context, otherOrganization, plan, DateTime.UtcNow.AddDays(10));
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();

        Assert.True(await service.HasActiveSubscriptionAsync(organization.OwnerUserId, organization.Id));

        var subscription = await context.OrganizationSubscriptions.FirstAsync(item => item.OrganizationId == organization.Id);
        subscription.EndDate = DateTime.UtcNow.AddDays(-1);
        await context.SaveChangesAsync();

        Assert.False(await service.HasActiveSubscriptionAsync(organization.OwnerUserId, organization.Id));
        Assert.True(await service.HasActiveSubscriptionAsync(otherOrganization.OwnerUserId, otherOrganization.Id));
    }

    [Fact]
    public async Task EndDateIsInclusiveAtTheExactExpiryInstant()
    {
        var expiryTicks = DateTime.UtcNow.Ticks;
        var expiry = new DateTime(expiryTicks - expiryTicks % 10, DateTimeKind.Utc).AddHours(1);
        await using var context = fixture.CreateContext(new ExactSubscriptionExpiryInterceptor(expiry));
        var organization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        var plan = await fixture.CreatePlanAsync(context, $"Граничный-{Guid.NewGuid():N}");
        await fixture.AddSubscriptionAsync(context, organization, plan, expiry);
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();

        Assert.True(await service.HasActiveSubscriptionAsync(organization.OwnerUserId, organization.Id));
        var current = await service.GetCurrentAsync(organization.OwnerUserId, organization.Id);
        Assert.NotNull(current);
        Assert.Equal(expiry, current.EndDate);
    }

    [Fact]
    public async Task EffectivePlanUsesFreeDefaultOnlyWhenThereIsNoCurrentSubscription()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        var defaultPlan = await fixture.CreatePlanAsync(context, $"Базовый-{Guid.NewGuid():N}", isDefault: true, vehicleLimit: 6);
        var paidPlan = await fixture.CreatePlanAsync(context, $"Платный-{Guid.NewGuid():N}", vehicleLimit: 28);
        await fixture.AddSubscriptionAsync(context, organization, paidPlan, DateTime.UtcNow.AddDays(5));
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();

        var effectivePlan = await service.GetEffectivePlanAsync(organization.OwnerUserId, organization.Id);

        Assert.NotNull(effectivePlan);
        Assert.Equal(paidPlan.Id, effectivePlan.Id);
        Assert.NotEqual(defaultPlan.Id, effectivePlan.Id);
    }

    [Fact]
    public async Task EffectivePlanMapsDefaultFreePlanWhenNoActiveSubscriptionExists()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        var defaultPlan = await fixture.CreatePlanAsync(context, $"Базовый-{Guid.NewGuid():N}", isDefault: true, vehicleLimit: 6);
        await fixture.AddSubscriptionAsync(context, organization,
            await fixture.CreatePlanAsync(context, $"Истекший-{Guid.NewGuid():N}"), DateTime.UtcNow.AddDays(-1));
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();

        var effectivePlan = await service.GetEffectivePlanAsync(organization.OwnerUserId, organization.Id);

        Assert.NotNull(effectivePlan);
        Assert.Equal(defaultPlan.Id, effectivePlan.Id);
        Assert.True(effectivePlan.IsDefaultPlan);
    }

    [Fact]
    public async Task SnapshotIncludesActiveOrganizationAndBothScopesInStableOrderAndAppliesAssignmentsAndDefaults()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        var enabled = await fixture.CreateFeatureAsync(context, $"test.enabled.{Guid.NewGuid():N}", "Включено", FeatureValueType.Boolean);
        var missingBoolean = await fixture.CreateFeatureAsync(context, $"test.missing.{Guid.NewGuid():N}", "Без значения", FeatureValueType.Boolean);
        var bothScope = await fixture.CreateFeatureAsync(context, $"test.both.{Guid.NewGuid():N}", "Оба пространства",
            FeatureValueType.Boolean, FeatureCatalogCodes.Scopes.Both);
        var integer = await fixture.CreateFeatureAsync(context, FeatureCodes.WorkspaceVehiclesLimit, "Лимит техники", FeatureValueType.Integer);
        var members = await fixture.CreateFeatureAsync(context, FeatureCodes.WorkspaceMembersLimit, "Лимит участников", FeatureValueType.Integer);
        var otherInteger = await fixture.CreateFeatureAsync(context, $"test.integer.{Guid.NewGuid():N}", "Число по умолчанию",
            FeatureValueType.Integer);
        var personal = await fixture.CreateFeatureAsync(context, $"test.personal.{Guid.NewGuid():N}", "Личное", FeatureValueType.Boolean,
            FeatureCatalogCodes.Scopes.Personal);
        var inactive = await fixture.CreateFeatureAsync(context, $"test.inactive.{Guid.NewGuid():N}", "Неактивно", FeatureValueType.Boolean,
            isActive: false);
        var defaultPlan = await fixture.CreatePlanAsync(context, $"Базовый-{Guid.NewGuid():N}", isDefault: true, vehicleLimit: 17,
            features: [(enabled, true, (int?)null), (members, null, 1)]);
        using var provider = this.CreateService(context, commercialEnabled: true);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();

        var snapshot = await service.GetFeatureSnapshotAsync(organization.OwnerUserId, organization.Id);

        Assert.Equal(new[] { "Без значения", "Включено", "Лимит техники", "Лимит участников", "Оба пространства", "Число по умолчанию" },
            snapshot.Features.Select(feature => feature.Name));
        Assert.DoesNotContain(snapshot.Features, feature => feature.FeatureId == personal.Id);
        Assert.DoesNotContain(snapshot.Features, feature => feature.FeatureId == inactive.Id);
        Assert.True(snapshot.Features.Single(feature => feature.FeatureId == enabled.Id).BoolValue);
        Assert.False(snapshot.Features.Single(feature => feature.FeatureId == missingBoolean.Id).BoolValue);
        Assert.False(snapshot.Features.Single(feature => feature.FeatureId == bothScope.Id).BoolValue);
        Assert.Equal(defaultPlan.VehicleLimit, snapshot.Features.Single(feature => feature.Code == FeatureCodes.WorkspaceVehiclesLimit).IntValue);
        Assert.Equal(1, snapshot.Features.Single(feature => feature.FeatureId == members.Id).IntValue);
        Assert.Equal(0, snapshot.Features.Single(feature => feature.FeatureId == otherInteger.Id).IntValue);
        Assert.Equal(defaultPlan.VehicleLimit, await service.GetVehicleLimitAsync(organization.OwnerUserId, organization.Id, 99));
        Assert.Equal(1, await service.GetMembersLimitAsync(organization.OwnerUserId, organization.Id, 99));
        Assert.True(await service.HasFeatureAsync(organization.OwnerUserId, organization.Id, enabled.Code));
        Assert.False(await service.HasFeatureAsync(organization.OwnerUserId, organization.Id, missingBoolean.Code));
        Assert.False(await service.HasFeatureAsync(organization.OwnerUserId, organization.Id, otherInteger.Code));
        Assert.False(await service.HasFeatureAsync(organization.OwnerUserId, organization.Id, "billing.feature.missing"));
    }

    [Fact]
    public async Task SnapshotUsesNeutralPlatformDefaultsAndEmptySnapshotWhenDefinitionsDoNotExist()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        var vehicles = await fixture.CreateFeatureAsync(context, FeatureCodes.WorkspaceVehiclesLimit,
            "Лимит техники", FeatureValueType.Integer);
        var members = await fixture.CreateFeatureAsync(context, FeatureCodes.WorkspaceMembersLimit,
            "Лимит участников", FeatureValueType.Integer);
        var platformFeature = await fixture.CreateFeatureAsync(
            context,
            FeatureCodes.Assistant,
            "Возможность платформы",
            FeatureValueType.Boolean);
        using var provider = this.CreateService(context, commercialEnabled: false);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();

        var snapshot = await service.GetFeatureSnapshotAsync(organization.OwnerUserId, organization.Id);

        Assert.Equal(PlatformDefaults.VehicleLimit, snapshot.Features.Single(feature => feature.FeatureId == vehicles.Id).IntValue);
        Assert.Equal(PlatformDefaults.WorkspaceMembersLimit, snapshot.Features.Single(feature => feature.FeatureId == members.Id).IntValue);
        Assert.True(snapshot.Features.Single(feature => feature.FeatureId == platformFeature.Id).BoolValue);
        Assert.Equal(PlatformDefaults.VehicleLimit, await service.GetVehicleLimitAsync(organization.OwnerUserId, organization.Id, 99));
        Assert.Equal(PlatformDefaults.WorkspaceMembersLimit, await service.GetMembersLimitAsync(organization.OwnerUserId, organization.Id, 99));
        Assert.True(await service.HasFeatureAsync(organization.OwnerUserId, organization.Id, platformFeature.Code));

        context.FeatureDefinitions.RemoveRange(await context.FeatureDefinitions.ToListAsync());
        await context.SaveChangesAsync();
        var emptySnapshot = await service.GetFeatureSnapshotAsync(organization.OwnerUserId, organization.Id);
        Assert.Empty(emptySnapshot.Features);
        Assert.Equal(0, await service.GetVehicleLimitAsync(organization.OwnerUserId, organization.Id, 99));
        Assert.Equal(0, await service.GetMembersLimitAsync(organization.OwnerUserId, organization.Id, 99));
    }

    [Fact]
    public async Task DemoIsAvailableForFirstEligibleWorkspaceAndDeniedForLaterMatchingScenario()
    {
        await using var context = fixture.CreateContext();
        var first = await fixture.CreateOrganizationAsync(context, scenario: "sto", createdAt: DateTime.UtcNow.AddDays(-2));
        var second = await fixture.CreateOrganizationAsync(context, ownerUserId: first.OwnerUserId, scenario: "sto", createdAt: DateTime.UtcNow.AddDays(-1));
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();

        var available = await service.CheckDemoAvailabilityAsync(first.OwnerUserId, first.Id);
        var denied = await service.CheckDemoAvailabilityAsync(second.OwnerUserId, second.Id);

        Assert.True(available.Available);
        Assert.Equal("Демо-тариф доступен.", available.Message);
        Assert.False(denied.Available);
        Assert.Equal("Демо-тариф доступен только для первого рабочего пространства такого типа.", denied.Message);
    }

    [Fact]
    public async Task EffectivePlanSnapshotChangesWhenNextPaidPeriodStarts()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        var feature = await fixture.CreateFeatureAsync(context, "organization-paid-period-feature", "Платная возможность",
            FeatureValueType.Boolean);
        var plan = await fixture.CreatePlanAsync(context, $"Платный период-{Guid.NewGuid():N}", features: [(feature, true, (int?)null)]);
        var now = DateTime.UtcNow;
        var boundary = now.AddDays(1);
        var currentPurchase = await fixture.AddPurchaseSnapshotAsync(context, organization.OwnerUserId, organization.Id, plan,
            SubscriptionPlanSnapshot.Capture(plan), now.AddDays(-10), boundary);
        plan.Features.Single().BoolValue = false;
        var nextPurchase = await fixture.AddPurchaseSnapshotAsync(context, organization.OwnerUserId, organization.Id, plan,
            SubscriptionPlanSnapshot.Capture(plan), boundary, now.AddDays(30));
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = plan.Id,
            SubscriptionPurchaseId = nextPurchase.Id,
            StartDate = now.AddDays(-10),
            EndDate = now.AddDays(30),
            SourceCode = "paid"
        });
        await context.SaveChangesAsync();
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();

        var current = await service.GetFeatureSnapshotAsync(organization.OwnerUserId, organization.Id);
        Assert.True(current.Features.Single(item => item.Code == feature.Code).BoolValue);

        currentPurchase.GrantedUntil = now.AddSeconds(-2);
        nextPurchase.GrantedFrom = now.AddSeconds(-2);
        await context.SaveChangesAsync();

        var next = await service.GetFeatureSnapshotAsync(organization.OwnerUserId, organization.Id);
        Assert.False(next.Features.Single(item => item.Code == feature.Code).BoolValue);
    }

    [Fact]
    public async Task DemoAvailabilityUsesCurrentGreatestSubscriptionAndSubscriptionHistory()
    {
        await using var context = fixture.CreateContext();
        var demoOrg = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        var paidOrg = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        var historyOrg = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        var demoPlan = await fixture.CreatePlanAsync(context, $"Демо-{Guid.NewGuid():N}", isDemo: true);
        var paidPlan = await fixture.CreatePlanAsync(context, $"Бизнес-{Guid.NewGuid():N}");
        await fixture.AddSubscriptionAsync(context, demoOrg, demoPlan, DateTime.UtcNow.AddDays(3));
        await fixture.AddSubscriptionAsync(context, paidOrg, paidPlan, DateTime.UtcNow.AddDays(6));
        await fixture.AddSubscriptionAsync(context, paidOrg, demoPlan, DateTime.UtcNow.AddDays(2));
        await fixture.AddSubscriptionAsync(context, historyOrg, paidPlan, DateTime.UtcNow.AddDays(-1));
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();

        var demo = await service.CheckDemoAvailabilityAsync(demoOrg.OwnerUserId, demoOrg.Id);
        var paid = await service.CheckDemoAvailabilityAsync(paidOrg.OwnerUserId, paidOrg.Id);
        var history = await service.CheckDemoAvailabilityAsync(historyOrg.OwnerUserId, historyOrg.Id);

        Assert.Equal((false, "Демо-тариф уже активен."), demo);
        Assert.Equal((false, "У рабочего пространства уже есть активный бизнес-тариф."), paid);
        Assert.Equal((false, "Демо-тариф доступен только рабочим пространствам без истории демо или платных тарифов."), history);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("archived")]
    [InlineData("inactive")]
    public async Task ReadAccessRejectsDeletedArchivedAndInactiveOrganizations(string state)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        switch (state)
        {
            case "deleted":
                organization.Delete(DateTime.UtcNow);
                break;
            case "archived":
                organization.Archive(DateTime.UtcNow);
                break;
            case "inactive":
                organization.IsActive = false;
                break;
        }

        await context.SaveChangesAsync();
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetFeatureSnapshotAsync(organization.OwnerUserId, organization.Id));

        Assert.Equal("Организация не найдена или доступ закрыт.", exception.Message);
    }

    [Fact]
    public async Task ReadAccessRejectsOrganizationWithoutMembershipAndUnknownOrganization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();

        var foreignMember = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetCurrentAsync($"foreign-{Guid.NewGuid():N}", organization.Id));
        var missingOrganization = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetCurrentAsync(organization.OwnerUserId, Guid.NewGuid()));

        Assert.Equal("Организация не найдена или доступ закрыт.", foreignMember.Message);
        Assert.Equal(foreignMember.Message, missingOrganization.Message);
    }

    [Fact]
    public async Task ReaderRevalidatesOrganizationExistenceWhenItIsCalledDirectly()
    {
        await using var context = fixture.CreateContext();
        var initializer = fixture.CreateDefaultsInitializer();
        using var provider = fixture.CreateServices(context, initializer.Object);
        using var scope = provider.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<OrganizationEntitlementReader>();
        var missingOrganizationId = Guid.NewGuid();

        var demo = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.CheckDemoAvailabilityAsync(missingOrganizationId));
        var entitlement = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetWorkspaceEntitlementAsync(missingOrganizationId));
        var plan = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetEffectivePlanAsync(missingOrganizationId));

        Assert.Equal("Организация не найдена.", demo.Message);
        Assert.Equal(demo.Message, entitlement.Message);
        Assert.Equal(demo.Message, plan.Message);
    }

    [Fact]
    public async Task WorkspaceEntitlementReportsBaseActiveAndHistoryFlags()
    {
        await using var context = fixture.CreateContext();
        await OpenCRM.Modules.Billing.Features.FeatureBootstrapper.EnsureDefaultsAsync(context);
        var owner = await fixture.CreateOrganizationAsync(context, scenario: "detailing");
        var otherScenario = await fixture.CreateOrganizationAsync(context, ownerUserId: owner.OwnerUserId, scenario: "sto");
        var sameScenario = await fixture.CreateOrganizationAsync(context, ownerUserId: owner.OwnerUserId, scenario: "detailing");
        var activePlan = await fixture.CreatePlanAsync(context, $"Активный-{Guid.NewGuid():N}");
        var historicalPlan = await fixture.CreatePlanAsync(context, $"Исторический-{Guid.NewGuid():N}");
        await fixture.AddSubscriptionAsync(context, owner, activePlan, DateTime.UtcNow.AddDays(5));
        await fixture.AddSubscriptionAsync(context, sameScenario, historicalPlan, DateTime.UtcNow.AddDays(-5));
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationEntitlementService>();

        var baseEntitlement = await service.GetWorkspaceEntitlementAsync(owner.OwnerUserId, owner.Id);
        var otherScenarioEntitlement = await service.GetWorkspaceEntitlementAsync(otherScenario.OwnerUserId, otherScenario.Id);
        var sameScenarioEntitlement = await service.GetWorkspaceEntitlementAsync(sameScenario.OwnerUserId, sameScenario.Id);

        Assert.True(baseEntitlement.IsBaseWorkspace);
        Assert.True(baseEntitlement.HasActiveSubscription);
        Assert.True(baseEntitlement.HasSubscriptionHistory);
        Assert.False(otherScenarioEntitlement.IsBaseWorkspace);
        Assert.False(otherScenarioEntitlement.HasActiveSubscription);
        Assert.False(otherScenarioEntitlement.HasSubscriptionHistory);
        Assert.False(sameScenarioEntitlement.IsBaseWorkspace);
        Assert.False(sameScenarioEntitlement.HasActiveSubscription);
        Assert.True(sameScenarioEntitlement.HasSubscriptionHistory);
    }

    private ServiceProvider CreateService(OpenCRMContext context, bool commercialEnabled = true)
    {
        var initializer = fixture.CreateDefaultsInitializer();
        var commercialPolicy = Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == commercialEnabled);

        return fixture.CreateServices(context, initializer.Object, platformCommercialPolicy: commercialPolicy);
    }
}
