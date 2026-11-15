using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Billing.Subscriptions;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Tests.Unit;

public sealed class PersonalSubscriptionFacadeTests
{
    [Fact]
    public async Task CompatibilityReadsDelegateWithoutDatabaseOrPaymentCalls()
    {
        await using var context = new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unit_model;Username=test;Password=test").Options);
        const string UserId = "actor";
        var query = new Mock<IPersonalSubscriptionQueryService>(MockBehavior.Strict);
        var catalog = new Mock<IPersonalSubscriptionCatalogQueryService>(MockBehavior.Strict);
        var activation = new Mock<IPersonalSubscriptionActivationService>(MockBehavior.Strict);
        var purchasePolicy = new Mock<IPersonalSubscriptionPurchasePolicyService>(MockBehavior.Strict);
        var vehicles = new Mock<IVehicleQueryService>(MockBehavior.Strict);
        var checkout = new Mock<IPersonalSubscriptionCheckoutService>(MockBehavior.Strict);
        var plan = new SubscriptionPlanDto { Id = 7 };
        var current = new UserSubscriptionDto { SubscriptionPlan = plan };
        IEnumerable<SubscriptionPlanDto> plans = [plan];
        IEnumerable<SubscriptionPlanGroupDto> groups = [new SubscriptionPlanGroupDto()];
        IEnumerable<SubscriptionHistoryDto> history = [new SubscriptionHistoryDto()];
        query.Setup(service => service.GetUserSubscriptionAsync(UserId)).ReturnsAsync(current);
        query.Setup(service => service.GetCombinedSubscriptionHistoryAsync(UserId)).ReturnsAsync(history);
        query.Setup(service => service.HasActiveSubscriptionAsync(UserId)).ReturnsAsync(true);
        query.Setup(service => service.GetUserVehicleLimitAsync(UserId)).ReturnsAsync(4);
        query.Setup(service => service.GetNextPlanAsync(UserId)).ReturnsAsync(plan);
        query.Setup(service => service.IsMaxPlanAsync(UserId)).ReturnsAsync(true);
        query.Setup(service => service.CanExtendSubscriptionAsync(UserId)).ReturnsAsync(true);
        query.Setup(service => service.GetSubscriptionExtensionInfoAsync(UserId)).ReturnsAsync((true, 2, "Продление"));
        query.Setup(service => service.CheckDemoSubscriptionAvailabilityAsync(UserId)).ReturnsAsync((false, "Демо"));
        query.Setup(service => service.EnsureDefaultPlanAccessAsync(UserId)).Returns(Task.CompletedTask);
        purchasePolicy.Setup(service => service.CanPurchasePlanAsync(UserId, 7)).ReturnsAsync(SubscriptionResult.CreateSuccess());
        activation.Setup(service => service.ActivateNewSubscriptionAsync(UserId, 7, "purchase"))
            .ReturnsAsync(SubscriptionResult.CreateSuccess());
        activation.Setup(service => service.ActivateExtendedSubscriptionAsync(UserId, 7, "extension"))
            .ReturnsAsync(SubscriptionResult.CreateSuccess());
        catalog.Setup(service => service.GetAvailablePlansAsync(UserId)).ReturnsAsync(plans);
        catalog.Setup(service => service.GetCatalogPlansAsync(UserId)).ReturnsAsync(plans);
        catalog.Setup(service => service.GetGroupedPlansAsync(UserId)).ReturnsAsync(groups);
        catalog.Setup(service => service.GetDefaultPlanAsync()).ReturnsAsync(plan);
        catalog.Setup(service => service.GetDemoPlanAsync()).ReturnsAsync(plan);
        vehicles.Setup(service => service.GetVehiclesCountAsync(UserId)).ReturnsAsync(3);
        var facade = new SubscriptionService(
            vehicles.Object,
            Mock.Of<IPersonalSubscriptionCommandService>(),
            checkout.Object,
            query.Object,
            catalog.Object,
            activation.Object,
            purchasePolicy.Object);

        Assert.Same(current, await facade.GetUserSubscriptionAsync(UserId));
        Assert.Same(history, await facade.GetCombinedSubscriptionHistoryAsync(UserId));
        Assert.True(await facade.HasActiveSubscriptionAsync(UserId));
        Assert.Equal(4, await facade.GetUserVehicleLimitAsync(UserId));
        Assert.True(await facade.CanUserAddVehicleAsync(UserId));
        Assert.Same(plan, await facade.GetNextPlanAsync(UserId));
        Assert.True(await facade.IsMaxPlanAsync(UserId));
        Assert.True(await facade.CanExtendSubscriptionAsync(UserId));
        Assert.Equal((true, 2, "Продление"), await facade.GetSubscriptionExtensionInfoAsync(UserId));
        Assert.Equal((false, "Демо"), await facade.CheckDemoSubscriptionAvailabilityAsync(UserId));
        await facade.EnsureDefaultPlanAccessAsync(UserId);
        Assert.Same(plans, await facade.GetAvailablePlansAsync(UserId));
        Assert.Same(plans, await facade.GetCatalogPlansAsync(UserId));
        Assert.Same(groups, await facade.GetGroupedPlansAsync(UserId));
        Assert.Same(plan, await facade.GetDefaultPlanAsync());
        Assert.Same(plan, await facade.GetDemoPlanAsync());
        Assert.True((await facade.CanPurchasePlanAsync(UserId, 7)).Success);
        Assert.True((await facade.ActivateNewSubscription(UserId, 7, "purchase")).Success);
        Assert.True((await facade.ActivateExtendedSubscription(UserId, 7, "extension")).Success);
        query.VerifyAll();
        catalog.VerifyAll();
        activation.VerifyAll();
        purchasePolicy.VerifyAll();
        vehicles.VerifyAll();
        checkout.VerifyNoOtherCalls();
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
