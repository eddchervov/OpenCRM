using Moq;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing.Tests.Unit;

public sealed class OrganizationSubscriptionFacadeTests
{
    [Fact]
    public async Task AllReadMethodsDelegateWithoutCallingPaymentProviderOrBillingCommands()
    {
        var organizationId = Guid.NewGuid();
        const string UserId = "actor";
        var entitlement = new Mock<IOrganizationEntitlementService>(MockBehavior.Strict);
        var access = new Mock<IWorkspaceAccessService>(MockBehavior.Strict);
        var catalog = new Mock<IOrganizationSubscriptionCatalogQueryService>(MockBehavior.Strict);
        var activation = new Mock<IOrganizationSubscriptionActivationService>(MockBehavior.Strict);
        var checkout = new Mock<IOrganizationSubscriptionCheckoutService>(MockBehavior.Strict);
        var purchasePolicy = new Mock<IOrganizationSubscriptionPurchasePolicyService>(MockBehavior.Strict);
        var extensionQuery = new Mock<IOrganizationSubscriptionExtensionQueryService>(MockBehavior.Strict);
        extensionQuery.Setup(service => service.CanExtendSubscriptionAsync(UserId, organizationId)).ReturnsAsync(true);
        extensionQuery.Setup(service => service.GetSubscriptionExtensionInfoAsync(UserId, organizationId)).ReturnsAsync((true, 3, "Продление"));
        var plan = new SubscriptionPlanDto { Id = 7 };
        var current = new OrganizationSubscriptionDto { SubscriptionPlan = plan };
        var snapshot = new FeatureAccessSnapshotDto();
        var workspace = new OrganizationWorkspaceAccessDto();
        entitlement.Setup(service => service.GetCurrentAsync(UserId, organizationId)).ReturnsAsync(current);
        entitlement.Setup(service => service.GetEffectivePlanAsync(UserId, organizationId)).ReturnsAsync(plan);
        entitlement.Setup(service => service.GetFeatureSnapshotAsync(UserId, organizationId)).ReturnsAsync(snapshot);
        entitlement.Setup(service => service.HasActiveSubscriptionAsync(UserId, organizationId)).ReturnsAsync(true);
        entitlement.Setup(service => service.HasFeatureAsync(UserId, organizationId, "feature")).ReturnsAsync(true);
        entitlement.Setup(service => service.GetVehicleLimitAsync(UserId, organizationId, 8)).ReturnsAsync(20);
        entitlement.Setup(service => service.GetMembersLimitAsync(UserId, organizationId, 3)).ReturnsAsync(10);
        entitlement.Setup(service => service.CheckDemoAvailabilityAsync(UserId, organizationId)).ReturnsAsync((true, "Доступно"));
        access.Setup(service => service.GetWorkspaceAccessAsync(UserId, organizationId)).ReturnsAsync(workspace);
        access.Setup(service => service.EnsureWorkspaceCanReadAsync(UserId, organizationId, "permission")).Returns(Task.CompletedTask);
        access.Setup(service => service.EnsureWorkspaceCanWriteAsync(UserId, organizationId)).Returns(Task.CompletedTask);
        access.Setup(service => service.EnsureWorkspaceCanWriteAsync(UserId, organizationId, "permission")).Returns(Task.CompletedTask);
        IReadOnlyList<SubscriptionPlanDto> plans = [plan];
        catalog.Setup(service => service.GetAvailablePlansAsync(UserId, organizationId, true)).ReturnsAsync(plans);
        var purchaseResult = SubscriptionResult.CreateSuccess();
        var activationResult = SubscriptionResult.CreateSuccess();
        purchasePolicy.Setup(service => service.CanPurchasePlanAsync(UserId, organizationId, 7)).ReturnsAsync(purchaseResult);
        activation.Setup(service => service.ActivateNewSubscriptionAsync(UserId, organizationId, 7, "purchase"))
            .ReturnsAsync(activationResult);
        activation.Setup(service => service.ActivateExtendedSubscriptionAsync(UserId, organizationId, 7, "extension"))
            .ReturnsAsync(activationResult);
        var service = new OrganizationSubscriptionService(
            catalog.Object,
            entitlement.Object,
            access.Object,
            checkout.Object,
            extensionQuery.Object,
            Mock.Of<IOrganizationSubscriptionCommandService>(),
            activation.Object,
            purchasePolicy.Object);

        Assert.Same(current, await service.GetCurrentAsync(UserId, organizationId));
        Assert.Same(plan, await service.GetEffectivePlanAsync(UserId, organizationId));
        Assert.Same(snapshot, await service.GetFeatureSnapshotAsync(UserId, organizationId));
        Assert.True(await service.HasActiveSubscriptionAsync(UserId, organizationId));
        Assert.True(await service.HasFeatureAsync(UserId, organizationId, "feature"));
        Assert.Equal(20, await service.GetVehicleLimitAsync(UserId, organizationId, 8));
        Assert.Equal(10, await service.GetMembersLimitAsync(UserId, organizationId, 3));
        Assert.Equal((true, "Доступно"), await service.CheckDemoAvailabilityAsync(UserId, organizationId));
        Assert.Same(workspace, await service.GetWorkspaceAccessAsync(UserId, organizationId));
        Assert.Same(plans, await service.GetAvailablePlansAsync(UserId, organizationId, true));
        await service.EnsureWorkspaceCanReadAsync(UserId, organizationId, "permission");
        await service.EnsureWorkspaceCanWriteAsync(UserId, organizationId);
        await service.EnsureWorkspaceCanWriteAsync(UserId, organizationId, "permission");
        Assert.Same(purchaseResult, await service.CanPurchasePlanAsync(UserId, organizationId, 7));
        Assert.Same(activationResult, await service.ActivateNewSubscriptionAsync(UserId, organizationId, 7, "purchase"));
        Assert.Same(activationResult, await service.ActivateExtendedSubscriptionAsync(UserId, organizationId, 7, "extension"));
        Assert.True(await service.CanExtendSubscriptionAsync(UserId, organizationId));
        Assert.Equal((true, 3, "Продление"), await service.GetSubscriptionExtensionInfoAsync(UserId, organizationId));
        extensionQuery.VerifyAll();
        entitlement.VerifyAll();
        access.VerifyAll();
        catalog.VerifyAll();
        activation.VerifyAll();
        purchasePolicy.VerifyAll();
    }
}
