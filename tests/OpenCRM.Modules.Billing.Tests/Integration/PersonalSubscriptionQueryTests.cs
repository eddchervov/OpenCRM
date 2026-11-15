using Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Billing.Contracts;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class PersonalSubscriptionQueryTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    [Theory]
    [InlineData(false, 2, true)]
    [InlineData(false, 8, false)]
    [InlineData(true, 2, false)]
    public async Task CurrentSubscriptionExtensionAndHistoryKeepUserAndPlanRules(bool isDemo, int days, bool canExtend)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var other = new ApplicationUser();
        context.Users.AddRange(user, other);
        var feature = await fixture.CreateFeatureAsync(context, "export-pdf", "Старое название", FeatureValueType.Boolean,
            FeatureCatalogCodes.Scopes.Personal);
        await fixture.CreateFeatureAsync(context, FeatureCodes.VehiclesLimit, "Техника", FeatureValueType.Integer,
            FeatureCatalogCodes.Scopes.Personal);
        var plan = await fixture.CreatePlanAsync(context, "Персональный", false, isDemo, 9, (feature, true, null));
        plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        plan.DiscountPrice = 200;
        var timestamp = DateTime.UtcNow;
        var now = new DateTime(timestamp.Ticks - timestamp.Ticks % 10, DateTimeKind.Utc);
        var subscription = new UserSubscription { UserId = user.Id, SubscriptionPlan = plan, AutoRenewal = true,
            StartDate = now.AddDays(1), EndDate = now.AddDays(days) };
        context.UserSubscriptions.Add(subscription);
        var history = new UserSubscriptionHistory { UserId = user.Id, SubscriptionPlan = plan,
            StartDate = now.AddDays(-30), EndDate = now, ActualEndDate = now.AddDays(-1),
            CreatedAt = now, Status = "replaced", Comment = "История пользователя" };
        context.UserSubscriptionHistory.AddRange(history, new UserSubscriptionHistory { UserId = user.Id, SubscriptionPlan = plan,
            CreatedAt = now.AddDays(-1), StartDate = now.AddDays(-60), EndDate = now.AddDays(-30), Status = "created" },
            new UserSubscriptionHistory { UserId = other.Id, SubscriptionPlan = plan,
                CreatedAt = now.AddDays(1), StartDate = now, EndDate = now.AddDays(30), Status = "created" });
        await context.SaveChangesAsync();
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionQueryService>();

        var current = await query.GetUserSubscriptionAsync(user.Id);
        Assert.NotNull(current);
        Assert.Equal(subscription.Id, current.Id);
        Assert.True(current.AutoRenewal);
        Assert.Equal("Экспорт PDF", Assert.Single(current.SubscriptionPlan.Features).Name);
        Assert.True(await query.HasActiveSubscriptionAsync(user.Id));
        Assert.False(await query.HasActiveSubscriptionAsync(other.Id));
        Assert.Equal(canExtend, await query.CanExtendSubscriptionAsync(user.Id));
        var extension = await query.GetSubscriptionExtensionInfoAsync(user.Id);
        Assert.Equal(canExtend, extension.CanExtend);
        Assert.Equal(days, extension.DaysUntilExpiry);
        Assert.Equal(isDemo ? "Демо-тариф нельзя продлить" : canExtend
            ? $"Доступно для продления. Осталось дней: {days}"
            : $"Продление будет доступно за 7 дней до окончания. Осталось дней: {days}", extension.Message);
        var items = (await query.GetCombinedSubscriptionHistoryAsync(user.Id)).ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal(history.Id, items[0].Id);
        Assert.Equal(history.ActualEndDate, items[0].ActualEndDate);
        Assert.Equal(history.Comment, items[0].Comment);
        Assert.Equal(history.Status, items[0].Status);
        Assert.Equal(200, items[0].Amount);
        var historicalFeature = Assert.Single(items[0].SubscriptionPlan.Features);
        Assert.Equal("Экспорт PDF", historicalFeature.Name);
        Assert.Null(historicalFeature.Category);
        Assert.Equal(new OpenCRM.Services.Interfaces.Features.Dtos.FeatureValueDto().ScopeCode, historicalFeature.ScopeCode);
        Assert.Equal(PlatformDefaults.VehicleLimit, await query.GetUserVehicleLimitAsync(other.Id));
        var demo = await query.CheckDemoSubscriptionAvailabilityAsync(user.Id);
        Assert.Equal(isDemo, demo.Available);
        Assert.Equal(isDemo ? "Демо-тариф уже активен." : "У вас уже есть активная платная подписка.", demo.Message);
    }

    [Fact]
    public async Task NoSubscriptionDefaultAccessAndDemoHistoryDoNotCreateSyntheticSubscriptions()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        var vehicles = await fixture.CreateFeatureAsync(context, FeatureCodes.VehiclesLimit, "Техника", FeatureValueType.Integer,
            FeatureCatalogCodes.Scopes.Personal);
        var defaults = fixture.CreateDefaultsInitializer();
        using var provider = fixture.CreateServices(context, defaults.Object);
        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionQueryService>();

        Assert.Null(await query.GetUserSubscriptionAsync(user.Id));
        Assert.False(await query.CanExtendSubscriptionAsync(user.Id));
        Assert.Equal((false, 0, "У вас нет активной подписки"), await query.GetSubscriptionExtensionInfoAsync(user.Id));
        Assert.Equal((true, "Демо-тариф доступен."), await query.CheckDemoSubscriptionAvailabilityAsync(user.Id));
        await query.EnsureDefaultPlanAccessAsync(" ");
        await query.EnsureDefaultPlanAccessAsync(user.Id);
        var plan = await fixture.CreatePlanAsync(context, "Бесплатный", true, false, 11, (vehicles, null, 13));
        plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        await context.SaveChangesAsync();
        await query.EnsureDefaultPlanAccessAsync(user.Id);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
        Assert.Equal(13, await query.GetUserVehicleLimitAsync(user.Id));
        context.UserSubscriptionHistory.Add(new UserSubscriptionHistory { UserId = user.Id, SubscriptionPlan = plan,
            StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();
        Assert.Equal((false, "Демо-тариф доступен только тем, кто еще не покупал платные тарифы."),
            await query.CheckDemoSubscriptionAvailabilityAsync(user.Id));
        context.UserSubscriptions.Add(new UserSubscription { UserId = user.Id, SubscriptionPlan = plan,
            StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddDays(1) });
        await context.SaveChangesAsync();
        await query.EnsureDefaultPlanAccessAsync(user.Id);
        Assert.Single(await context.UserSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task NextAndMaximumUseOnlyAnonymousPersonalCatalogAndPreserveEmptyCatalogException()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionQueryService>();

        Assert.Null(await query.GetNextPlanAsync(user.Id));
        Assert.False(await query.IsMaxPlanAsync(user.Id));
        var basic = await fixture.CreatePlanAsync(context, "Первый");
        basic.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        basic.Order = 1;
        var premium = await fixture.CreatePlanAsync(context, "Следующий");
        premium.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        premium.Order = 2;
        var demo = await fixture.CreatePlanAsync(context, "Демо", false, true);
        demo.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        demo.Order = 3;
        await context.SaveChangesAsync();
        Assert.Equal(basic.Id, (await query.GetNextPlanAsync(user.Id))!.Id);
        var subscription = new UserSubscription { UserId = user.Id, SubscriptionPlan = basic,
            StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddDays(2) };
        context.UserSubscriptions.Add(subscription);
        await context.SaveChangesAsync();
        Assert.Equal(premium.Id, (await query.GetNextPlanAsync(user.Id))!.Id);
        Assert.False(await query.IsMaxPlanAsync(user.Id));
        subscription.SubscriptionPlan = premium;
        subscription.SubscriptionPlanId = premium.Id;
        await context.SaveChangesAsync();
        Assert.Null(await query.GetNextPlanAsync(user.Id));
        Assert.True(await query.IsMaxPlanAsync(user.Id));
        basic.IsActive = false;
        premium.IsActive = false;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => query.IsMaxPlanAsync(user.Id));
    }

    [Fact]
    public async Task RealDefaultsInitializerPopulatesConfiguredFreePlanWithoutCreatingSubscription()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        var configuredPlan = await fixture.CreatePlanAsync(context, "Личный бесплатный", true, false, 6);
        configuredPlan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        await context.SaveChangesAsync();
        using var provider = fixture.CreateServices(context, new OpenCRM.Testing.OrganizationBillingTestServices.DefaultsInitializer(context));
        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionQueryService>();
        var catalog = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionCatalogQueryService>();

        await query.EnsureDefaultPlanAccessAsync(user.Id);

        var defaults = await catalog.GetDefaultPlanAsync();
        Assert.NotNull(defaults);
        Assert.Equal(configuredPlan.Id, defaults.Id);
        Assert.True(defaults.IsDefaultPlan);
        Assert.Equal(SubscriptionPlanCatalogCodes.PlanTypes.Personal, defaults.PlanTypeCode);
        var vehicleFeature = Assert.Single(defaults.Features, feature => feature.Code == FeatureCodes.VehiclesLimit);
        Assert.Equal(vehicleFeature.IntValue ?? defaults.VehicleLimit, await query.GetUserVehicleLimitAsync(user.Id));
        Assert.NotEmpty(defaults.Features);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
        Assert.False(await query.HasActiveSubscriptionAsync(user.Id));
    }

    [Fact]
    public async Task SubscriptionExpiryIncludesExactInstantAndExcludesPreviousMicrosecond()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        var plan = await fixture.CreatePlanAsync(context, "Граница срока");
        plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        context.UserSubscriptions.Add(new UserSubscription { UserId = user.Id, SubscriptionPlan = plan,
            StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(1) });
        await context.SaveChangesAsync();
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionQueryService>();

        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "UserSubscriptions" SET "EndDate" = transaction_timestamp() WHERE "UserId" = {user.Id}
                """);

            Assert.True(await query.HasActiveSubscriptionAsync(user.Id));
            Assert.NotNull(await query.GetUserSubscriptionAsync(user.Id));
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "UserSubscriptions" SET "EndDate" = transaction_timestamp() - interval '1 microsecond' WHERE "UserId" = {user.Id}
                """);

            Assert.False(await query.HasActiveSubscriptionAsync(user.Id));
            Assert.Null(await query.GetUserSubscriptionAsync(user.Id));
            await transaction.RollbackAsync();
        });
    }

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
