using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Promotions.Dtos;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class OrganizationSubscriptionCatalogQueryTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AvailablePlansFilterUnsupportedRowsAndApplyStableOrderAndPromotions()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var first = await this.CreatePlanAsync(context, "Порядок-1 без скидки", order: 1, price: 100, discountPrice: 150);
        var second = await this.CreatePlanAsync(context, "Порядок-1 со скидкой", order: 1, price: 100, discountPrice: 40);
        var third = await this.CreatePlanAsync(context, "Порядок-2", order: 2, price: 10);
        var demo = await this.CreatePlanAsync(context, "Демо", order: 3, isDemo: true, price: 0);
        await this.CreatePlanAsync(context, "Базовый", isDefault: true);
        await this.CreatePlanAsync(context, "Неактивный", isActive: false);
        await this.CreatePlanAsync(context, "Личный", planTypeCode: SubscriptionPlanCatalogCodes.PlanTypes.Personal);
        var promotions = fixture.CreatePromoCodeService();
        promotions.Setup(service => service.CalculatePriceAsync(
                organization.OwnerUserId, organization.Id, It.IsAny<int>(), It.IsAny<decimal>()))
            .ReturnsAsync((string _, Guid _, int _, decimal price) => new PromotionPriceDto
            {
                OriginalPrice = price,
                FinalPrice = price - 5,
                DiscountAmount = 5,
                PromoCodeId = 16,
                PromoCode = "SPRING"
            });
        using var provider = this.CreateService(context, promotions.Object);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCatalogQueryService>();

        var plans = await service.GetAvailablePlansAsync(organization.OwnerUserId, organization.Id);

        Assert.Equal(new[] { second.Id, first.Id, third.Id }, plans.Select(plan => plan.Id));
        Assert.Equal(40, plans[0].OriginalEffectivePrice);
        Assert.Equal(35, plans[0].EffectivePrice);
        Assert.True(plans[0].HasPromoDiscount);
        Assert.Equal("SPRING", plans[0].PromoCode);
        Assert.DoesNotContain(plans, plan => plan.Id == demo.Id);
        promotions.Verify(service => service.CalculatePriceAsync(organization.OwnerUserId, organization.Id,
            It.IsAny<int>(), It.IsAny<decimal>()), Times.Exactly(3));
    }

    [Fact]
    public async Task UnavailableDemoIsIncludedOnlyWhenRequestedByAuthorizedTestAccount()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var business = await this.CreatePlanAsync(context, "Платный");
        var demo = await this.CreatePlanAsync(context, "Демо", isDemo: true, price: 0);
        var expired = await this.CreatePlanAsync(context, "Исторический", isActive: false);
        await fixture.AddSubscriptionAsync(context, organization, expired, DateTime.UtcNow.AddDays(-1));
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCatalogQueryService>();

        var ordinaryResults = await service.GetAvailablePlansAsync(organization.OwnerUserId, organization.Id,
            includeUnavailableDemoForTesting: true);
        Assert.Equal(new[] { business.Id }, ordinaryResults.Select(plan => plan.Id));

        var owner = await context.ApplicationUsers.FirstAsync(user => user.Id == organization.OwnerUserId);
        owner.CanUseTestSubscriptions = true;
        await context.SaveChangesAsync();

        var authorizedResults = await service.GetAvailablePlansAsync(organization.OwnerUserId, organization.Id,
            includeUnavailableDemoForTesting: true);

        Assert.Equal(new[] { demo.Id, business.Id }, authorizedResults.Select(plan => plan.Id));
    }

    private async Task<SubscriptionPlan> CreatePlanAsync(
        OpenCRMContext context,
        string name,
        int order = 0,
        decimal price = 100,
        decimal? discountPrice = null,
        bool isActive = true,
        bool isDefault = false,
        bool isDemo = false,
        string planTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business)
    {
        var plan = new SubscriptionPlan
        {
            Name = name,
            Description = name,
            Price = price,
            DiscountPrice = discountPrice,
            DurationDays = 30,
            IsActive = isActive,
            VehicleLimit = 10,
            Order = order,
            Group = name,
            PlanTypeCode = planTypeCode,
            IsDefaultPlan = isDefault,
            IsDemoPlan = isDemo
        };
        context.SubscriptionPlans.Add(plan);
        await context.SaveChangesAsync();

        return plan;
    }

    private ServiceProvider CreateService(OpenCRMContext context, Services.Interfaces.Promotions.IPromoCodeService? promoCodeService = null)
    {
        var initializer = fixture.CreateDefaultsInitializer();

        return fixture.CreateServices(context, initializer.Object, promoCodeService);
    }
}
