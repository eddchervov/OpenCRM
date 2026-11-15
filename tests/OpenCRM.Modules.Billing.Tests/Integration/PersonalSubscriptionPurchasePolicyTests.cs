using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class PersonalSubscriptionPurchasePolicyTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PurchasePolicyPreservesPlanAndCurrentSubscriptionRules()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var inactive = await this.CreatePlanAsync(context, "Неактивный", 1);
        inactive.IsActive = false;
        var business = await this.CreatePlanAsync(context, "Организация", 2);
        business.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business;
        var defaultPlan = await this.CreatePlanAsync(context, "Базовый", 3);
        defaultPlan.IsDefaultPlan = true;
        var demoPlan = await this.CreatePlanAsync(context, "Демо", 4);
        demoPlan.IsDemoPlan = true;
        var currentPlan = await this.CreatePlanAsync(context, "Текущий", 5);
        var lowerPlan = await this.CreatePlanAsync(context, "Ниже", 4);
        var higherPlan = await this.CreatePlanAsync(context, "Выше", 6);
        await context.SaveChangesAsync();
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionPurchasePolicyService>();

        Assert.False((await service.CanPurchasePlanAsync(user.Id, int.MaxValue)).Success);
        Assert.False((await service.CanPurchasePlanAsync(user.Id, inactive.Id)).Success);
        Assert.False((await service.CanPurchasePlanAsync(user.Id, business.Id)).Success);
        Assert.False((await service.CanPurchasePlanAsync(user.Id, defaultPlan.Id)).Success);
        Assert.False((await service.CanPurchasePlanAsync(user.Id, demoPlan.Id)).Success);
        Assert.True((await service.CanPurchasePlanAsync(user.Id, higherPlan.Id)).Success);

        context.UserSubscriptions.Add(new UserSubscription
        {
            UserId = user.Id,
            SubscriptionPlanId = currentPlan.Id,
            StartDate = DateTime.UtcNow.AddDays(-20),
            EndDate = DateTime.UtcNow.AddDays(10),
            SourceCode = "paid"
        });
        await context.SaveChangesAsync();

        Assert.False((await service.CanPurchasePlanAsync(user.Id, currentPlan.Id)).Success);
        Assert.False((await service.CanPurchasePlanAsync(user.Id, lowerPlan.Id)).Success);
        Assert.True((await service.CanPurchasePlanAsync(user.Id, higherPlan.Id)).Success);
    }

    private async Task<SubscriptionPlan> CreatePlanAsync(OpenCRMContext context, string name, int order)
    {
        var plan = await fixture.CreatePlanAsync(context, name);
        plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        plan.Order = order;
        await context.SaveChangesAsync();

        return plan;
    }
}
