using Moq;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Billing.Subscriptions;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Tests.Unit;

public sealed class PersonalSubscriptionCommandFacadeDelegationTests
{
    [Fact]
    public async Task SubscriptionCommandsDelegateToBillingWithoutDatabaseAccess()
    {
        const string UserId = "billing-user";
        const string AdminName = "Admin";
        const int PlanId = 12;
        var commandService = new Mock<IPersonalSubscriptionCommandService>(MockBehavior.Strict);
        commandService.Setup(service => service.ActivateSubscriptionForTestingAsync(UserId, PlanId))
            .ReturnsAsync(SubscriptionResult.CreateSuccess());
        commandService.Setup(service => service.ExtendSubscriptionForTestingAsync(UserId))
            .ReturnsAsync(SubscriptionResult.CreateSuccess());
        commandService.Setup(service => service.CancelSubscriptionForTestingAsync(UserId))
            .ReturnsAsync(SubscriptionResult.CreateSuccess());
        commandService.Setup(service => service.GrantDemoSubscriptionAsync(UserId))
            .ReturnsAsync(SubscriptionResult.CreateSuccess());
        commandService.Setup(service => service.GrantDemoSubscriptionByAdminAsync(UserId))
            .ReturnsAsync(SubscriptionResult.CreateSuccess());
        commandService.Setup(service => service.AssignSubscriptionByAdminAsync(UserId, PlanId, AdminName))
            .ReturnsAsync(SubscriptionResult.CreateSuccess());
        commandService.Setup(service => service.ReturnUserToDefaultPlanByAdminAsync(UserId, AdminName))
            .ReturnsAsync(SubscriptionResult.CreateSuccess());
        commandService.Setup(service => service.CheckAndDeactivateExpiredSubscriptionsAsync())
            .Returns(Task.CompletedTask);
        var facade = new SubscriptionService(
            Mock.Of<IVehicleQueryService>(),
            commandService.Object,
            Mock.Of<IPersonalSubscriptionCheckoutService>(),
            Mock.Of<IPersonalSubscriptionQueryService>(),
            Mock.Of<IPersonalSubscriptionCatalogQueryService>(),
            Mock.Of<IPersonalSubscriptionActivationService>(),
            Mock.Of<IPersonalSubscriptionPurchasePolicyService>());

        Assert.True((await facade.ActivateSubscriptionForTestingAsync(UserId, PlanId)).Success);
        Assert.True((await facade.ExtendSubscriptionForTestingAsync(UserId)).Success);
        Assert.True((await facade.CancelSubscriptionForTestingAsync(UserId)).Success);
        Assert.True((await facade.GrantDemoSubscriptionAsync(UserId)).Success);
        Assert.True((await facade.GrantDemoSubscriptionByAdminAsync(UserId)).Success);
        Assert.True((await facade.AssignSubscriptionByAdminAsync(UserId, PlanId, AdminName)).Success);
        Assert.True((await facade.ReturnUserToDefaultPlanByAdminAsync(UserId, AdminName)).Success);
        await facade.CheckAndDeactivateExpiredSubscriptionsAsync();

        commandService.VerifyAll();
    }
}
