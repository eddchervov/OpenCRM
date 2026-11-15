using Helpers;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Features;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class PersonalFeatureAccessTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    [Fact]
    public async Task GrantsRespectValuePriorityDatesAndImmediateRevocation()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        var boolean = await fixture.CreateFeatureAsync(context, "personal-boolean", "Булево", FeatureValueType.Boolean,
            FeatureCatalogCodes.Scopes.Personal);
        var integer = await fixture.CreateFeatureAsync(context, "personal-integer", "Число", FeatureValueType.Integer,
            FeatureCatalogCodes.Scopes.Personal);
        var now = DateTime.UtcNow;
        var grants = new[]
        {
            new UserFeatureGrant { UserId = user.Id, FeatureDefinitionId = boolean.Id, BoolValue = true, CreatedAt = now.AddDays(-2) },
            new UserFeatureGrant { UserId = user.Id, FeatureDefinitionId = boolean.Id, BoolValue = false, CreatedAt = now.AddDays(-1), StartDate = now.AddDays(-2), EndDate = now.AddDays(2) },
            new UserFeatureGrant { UserId = user.Id, FeatureDefinitionId = boolean.Id, CreatedAt = now },
            new UserFeatureGrant { UserId = user.Id, FeatureDefinitionId = boolean.Id, BoolValue = true, CreatedAt = now.AddDays(1), StartDate = now.AddDays(1) },
            new UserFeatureGrant { UserId = user.Id, FeatureDefinitionId = boolean.Id, BoolValue = true, CreatedAt = now.AddDays(1), EndDate = now.AddDays(-1) },
            new UserFeatureGrant { UserId = user.Id, FeatureDefinitionId = boolean.Id, BoolValue = true, CreatedAt = now.AddDays(1), IsEnabled = false },
            new UserFeatureGrant { UserId = user.Id, FeatureDefinitionId = integer.Id, IntValue = 15, CreatedAt = now.AddDays(-1) },
            new UserFeatureGrant { UserId = user.Id, FeatureDefinitionId = integer.Id, IntValue = 3, CreatedAt = now },
            new UserFeatureGrant { UserId = user.Id, FeatureDefinitionId = integer.Id, CreatedAt = now }
        };
        context.UserFeatureGrants.AddRange(grants);
        await context.SaveChangesAsync();
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFeatureAccessService>();

        Assert.False(await service.HasFeatureAsync(user.Id, boolean.Code));
        Assert.Equal(15, await service.GetIntValueAsync(user.Id, integer.Code));
        grants[1].IsEnabled = false;
        grants[6].IntValue = int.MinValue;
        grants[7].IntValue = null;
        await context.SaveChangesAsync();
        Assert.True(await service.HasFeatureAsync(user.Id, boolean.Code));
        Assert.Equal(29, await service.GetIntValueAsync(user.Id, integer.Code, 29));
        Assert.Equal(29, await service.GetIntValueAsync("other", integer.Code, 29));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlanAssignmentsAndFallbacksPreservePersonalSemantics(bool hasSubscription)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        var boolean = await fixture.CreateFeatureAsync(context, "assigned-bool", "А", FeatureValueType.Boolean, FeatureCatalogCodes.Scopes.Personal);
        var integer = await fixture.CreateFeatureAsync(context, "assigned-int", "Б", FeatureValueType.Integer, FeatureCatalogCodes.Scopes.Both);
        var vehicles = await fixture.CreateFeatureAsync(context, FeatureCodes.VehiclesLimit, "В", FeatureValueType.Integer, FeatureCatalogCodes.Scopes.Personal);
        var missing = await fixture.CreateFeatureAsync(context, "unassigned-int", "Г", FeatureValueType.Integer, FeatureCatalogCodes.Scopes.Personal);
        await fixture.CreateFeatureAsync(context, "organization-only", "Д", FeatureValueType.Boolean);
        await fixture.CreateFeatureAsync(context, "inactive", "Е", FeatureValueType.Boolean, FeatureCatalogCodes.Scopes.Personal, false);
        var plan = await fixture.CreatePlanAsync(context, "Личный", !hasSubscription, false, 17,
            (boolean, true, null), (integer, null, 12), (vehicles, null, null));
        plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        if (hasSubscription)
        {
            context.UserSubscriptions.Add(new UserSubscription { UserId = user.Id, SubscriptionPlan = plan,
                StartDate = DateTime.UtcNow.AddDays(1), EndDate = DateTime.UtcNow.AddDays(2) });
        }

        await context.SaveChangesAsync();
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFeatureAccessService>();

        Assert.True(await service.HasFeatureAsync(user.Id, boolean.Code));
        Assert.Equal(12, await service.GetIntValueAsync(user.Id, integer.Code));
        Assert.Equal(17, await service.GetIntValueAsync(user.Id, vehicles.Code));
        Assert.Equal(31, await service.GetIntValueAsync(user.Id, missing.Code, 31));
        var snapshot = await service.GetSnapshotAsync(user.Id);
        Assert.Equal(new[] { boolean.Code, integer.Code, vehicles.Code, missing.Code }, snapshot.Features.Select(value => value.Code));
        Assert.Equal(integer.ScopeCode, snapshot.Features[1].ScopeCode);
        Assert.Equal(integer.AudienceCode, snapshot.Features[1].AudienceCode);
        Assert.False(snapshot.Features[3].BoolValue);

        plan.Features[0].BoolValue = null;
        plan.Features[1].IntValue = null;
        await context.SaveChangesAsync();
        Assert.False(await service.HasFeatureAsync(user.Id, boolean.Code));
        Assert.Equal(31, await service.GetIntValueAsync(user.Id, integer.Code, 31));
        if (hasSubscription)
        {
            var subscription = context.UserSubscriptions.Local.Single();
            subscription.EndDate = DateTime.UtcNow.AddDays(-1);
            await context.SaveChangesAsync();
            Assert.Equal(PlatformDefaults.VehicleLimit, await service.GetIntValueAsync(user.Id, vehicles.Code));
        }
    }

    [Fact]
    public async Task MissingPlanAndUnknownOrInactiveFeatureHaveExplicitFallbacks()
    {
        await using var context = fixture.CreateContext();
        var vehicles = await fixture.CreateFeatureAsync(context, FeatureCodes.VehiclesLimit, "Техника", FeatureValueType.Integer,
            FeatureCatalogCodes.Scopes.Personal);
        var boolean = await fixture.CreateFeatureAsync(context, "fallback-bool", "Функция", FeatureValueType.Boolean,
            FeatureCatalogCodes.Scopes.Personal);
        await fixture.CreateFeatureAsync(context, "inactive", "Неактивна", FeatureValueType.Boolean, FeatureCatalogCodes.Scopes.Personal, false);
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFeatureAccessService>();

        Assert.Equal(PlatformDefaults.VehicleLimit, await service.GetIntValueAsync("unknown", vehicles.Code));
        Assert.False(await service.HasFeatureAsync("unknown", boolean.Code));
        foreach (var code in new[] { "unknown", "inactive" })
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.HasFeatureAsync("unknown", code));
            Assert.Equal($"Не найдена активная фича с кодом '{code}'.", exception.Message);
        }
    }

    [Fact]
    public async Task ExtensionSnapshotStartsAfterCurrentPaidPeriodEnds()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        var feature = await fixture.CreateFeatureAsync(context, "paid-period-feature", "Платная возможность", FeatureValueType.Boolean,
            FeatureCatalogCodes.Scopes.Personal);
        var plan = await fixture.CreatePlanAsync(context, "Платный период", features: [(feature, true, (int?)null)]);
        var now = DateTime.UtcNow;
        var boundary = now.AddDays(1);
        var currentPurchase = await fixture.AddPurchaseSnapshotAsync(context, user.Id, null, plan,
            SubscriptionPlanSnapshot.Capture(plan), now.AddDays(-10), boundary);
        plan.Features.Single().BoolValue = false;
        var nextPurchase = await fixture.AddPurchaseSnapshotAsync(context, user.Id, null, plan,
            SubscriptionPlanSnapshot.Capture(plan), boundary, now.AddDays(30));
        context.UserSubscriptions.Add(new UserSubscription
        {
            UserId = user.Id,
            SubscriptionPlanId = plan.Id,
            SubscriptionPurchaseId = nextPurchase.Id,
            StartDate = now.AddDays(-10),
            EndDate = now.AddDays(30),
            SourceCode = "paid"
        });
        await context.SaveChangesAsync();
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFeatureAccessService>();
        var query = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionQueryService>();

        Assert.True(await service.HasFeatureAsync(user.Id, feature.Code));
        Assert.True((await query.GetUserSubscriptionAsync(user.Id))!.SubscriptionPlan.Features.Single(item => item.Code == feature.Code).BoolValue);

        currentPurchase.GrantedUntil = now.AddSeconds(-2);
        nextPurchase.GrantedFrom = now.AddSeconds(-2);
        await context.SaveChangesAsync();

        Assert.False(await service.HasFeatureAsync(user.Id, feature.Code));
        Assert.False((await query.GetUserSubscriptionAsync(user.Id))!.SubscriptionPlan.Features.Single(item => item.Code == feature.Code).BoolValue);
    }

    [Theory]
    [InlineData("changed_plan")]
    [InlineData("changed_source")]
    public async Task PurchaseSnapshotDoesNotOverrideManualPlanOrSourceChange(string change)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        var feature = await fixture.CreateFeatureAsync(context, "manual-plan-feature", "Платная возможность", FeatureValueType.Boolean,
            FeatureCatalogCodes.Scopes.Personal);
        var paidPlan = await fixture.CreatePlanAsync(context, "Оплаченный тариф", features: [(feature, true, (int?)null)]);
        var now = DateTime.UtcNow;
        var purchase = await fixture.AddPurchaseSnapshotAsync(context, user.Id, null, paidPlan,
            SubscriptionPlanSnapshot.Capture(paidPlan), now.AddDays(-1), now.AddDays(10));
        paidPlan.Features.Single().BoolValue = false;
        var currentPlan = change == "changed_plan"
            ? await fixture.CreatePlanAsync(context, "Тариф назначенный вручную", features: [(feature, false, (int?)null)])
            : paidPlan;
        context.UserSubscriptions.Add(new UserSubscription
        {
            UserId = user.Id,
            SubscriptionPlanId = currentPlan.Id,
            SubscriptionPurchaseId = purchase.Id,
            StartDate = now.AddDays(-1),
            EndDate = now.AddDays(10),
            SourceCode = change == "changed_source" ? "promo" : "paid"
        });
        await context.SaveChangesAsync();
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFeatureAccessService>();
        var query = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionQueryService>();

        Assert.False(await service.HasFeatureAsync(user.Id, feature.Code));
        Assert.False((await query.GetUserSubscriptionAsync(user.Id))!.SubscriptionPlan.Features.Single(item => item.Code == feature.Code).BoolValue);
    }

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
