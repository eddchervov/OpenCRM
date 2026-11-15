using Moq;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Tests.Unit;

public sealed class OrganizationSubscriptionCommandFacadeTests
{
    [Fact]
    public async Task DemoAndTestingCommandsDelegateArgumentsAndResults()
    {
        const string userId = "actor";
        var organizationId = Guid.NewGuid();
        const int planId = 7;
        const int durationDays = 14;
        var entitlement = new Mock<IOrganizationEntitlementService>(MockBehavior.Strict);
        var command = new Mock<IOrganizationSubscriptionCommandService>(MockBehavior.Strict);
        var access = Mock.Of<IWorkspaceAccessService>();
        var checkout = Mock.Of<IOrganizationSubscriptionCheckoutService>();
        var activation = Mock.Of<IOrganizationSubscriptionActivationService>();
        var purchasePolicy = Mock.Of<IOrganizationSubscriptionPurchasePolicyService>();
        var catalog = Mock.Of<IOrganizationSubscriptionCatalogQueryService>();
        var demoResult = SubscriptionResult.CreateSuccess();
        var activationResult = SubscriptionResult.CreateFailed("Тестовая активация недоступна.");
        var extensionResult = SubscriptionResult.CreateSuccess();
        command.Setup(service => service.ActivateDemoAsync(userId, organizationId, planId, durationDays))
            .ReturnsAsync(demoResult);
        command.Setup(service => service.ActivateForTestingAsync(userId, organizationId, planId))
            .ReturnsAsync(activationResult);
        command.Setup(service => service.ExtendSubscriptionForTestingAsync(userId, organizationId))
            .ReturnsAsync(extensionResult);
        var service = new OrganizationSubscriptionService(
            catalog,
            entitlement.Object,
            access,
            checkout,
            Mock.Of<IOrganizationSubscriptionExtensionQueryService>(),
            command.Object,
            activation,
            purchasePolicy);

        Assert.Same(demoResult, await service.ActivateDemoAsync(userId, organizationId, planId, durationDays));
        Assert.Same(activationResult, await service.ActivateForTestingAsync(userId, organizationId, planId));
        Assert.Same(extensionResult, await service.ExtendSubscriptionForTestingAsync(userId, organizationId));
        command.VerifyAll();
    }
}
