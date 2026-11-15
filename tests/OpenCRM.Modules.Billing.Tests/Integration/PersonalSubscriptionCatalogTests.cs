using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Billing.Contracts;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class PersonalSubscriptionCatalogTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    [Fact]
    public async Task CatalogKeepsAllTypesWhileAvailableAndGroupsOnlyContainPersonalPlans()
    {
        await using var context = fixture.CreateContext();
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionCatalogQueryService>();

        Assert.Null(await catalog.GetDefaultPlanAsync());
        Assert.Null(await catalog.GetDemoPlanAsync());
        var business = await fixture.CreatePlanAsync(context, "Бизнес");
        var defaults = await fixture.CreatePlanAsync(context, "Базовый", true);
        defaults.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        var demo = await fixture.CreatePlanAsync(context, "Демо", false, true);
        demo.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        var inactive = await fixture.CreatePlanAsync(context, "Неактивный");
        inactive.IsActive = false;
        var personalPlans = new List<SubscriptionPlan>();
        foreach (var group in new string?[] { null, "", "Популярные тарифы", "Прочие", "Другие" })
        {
            var plan = await fixture.CreatePlanAsync(context, $"Личный-{personalPlans.Count}");
            plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
            plan.Group = group;
            plan.Price = 400;
            plan.DiscountPrice = personalPlans.Count == 0 ? 100 : personalPlans.Count == 1 ? 500 : null;
            personalPlans.Add(plan);
        }

        await context.SaveChangesAsync();
        var allPlans = (await catalog.GetCatalogPlansAsync()).ToArray();
        Assert.Equal(personalPlans.Count + 1, allPlans.Length);
        Assert.Contains(allPlans, plan => plan.Id == business.Id);
        Assert.DoesNotContain(allPlans, plan => plan.IsDemoPlan || plan.IsDefaultPlan || !plan.IsActive);
        Assert.Equal(personalPlans[0].Id, allPlans[0].Id);
        var available = (await catalog.GetAvailablePlansAsync()).ToArray();
        Assert.Equal(personalPlans.Count, available.Length);
        Assert.All(available, plan => Assert.Equal(SubscriptionPlanCatalogCodes.PlanTypes.Personal, plan.PlanTypeCode));
        var groups = (await catalog.GetGroupedPlansAsync()).ToArray();
        Assert.Equal(new[] { "Популярные тарифы", "Основные тарифы", "Другие", "Прочие" }, groups.Select(group => group.GroupName));
        Assert.Equal(2, groups[1].Plans.Count());
        Assert.Equal(personalPlans.Take(2).Select(plan => plan.Id), groups[1].Plans.Select(plan => plan.Id));
        Assert.Equal(defaults.Id, (await catalog.GetDefaultPlanAsync())!.Id);
        Assert.Equal(demo.Id, (await catalog.GetDemoPlanAsync())!.Id);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public async Task DemoVisibilityUsesHistoryAndTestPermission(bool testUser, bool paidHistory, bool includeDemo)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { CanUseTestSubscriptions = testUser };
        context.Users.Add(user);
        var demo = await fixture.CreatePlanAsync(context, "Демо", false, true);
        demo.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        var paid = await fixture.CreatePlanAsync(context, "Платный");
        paid.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        context.UserSubscriptionHistory.Add(new UserSubscriptionHistory { UserId = user.Id,
            SubscriptionPlan = paidHistory ? paid : demo, StartDate = DateTime.UtcNow.AddDays(-2),
            EndDate = DateTime.UtcNow.AddDays(-1), CreatedAt = DateTime.UtcNow.AddDays(-2) });
        await context.SaveChangesAsync();
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionCatalogQueryService>();

        Assert.Equal(includeDemo, (await catalog.GetCatalogPlansAsync(user.Id)).Any(plan => plan.Id == demo.Id));
        Assert.DoesNotContain(await catalog.GetCatalogPlansAsync(" "), plan => plan.IsDemoPlan);
    }

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
