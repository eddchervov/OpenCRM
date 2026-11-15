using Moq;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Subscriptions;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Modules.Billing.Tests.Unit;

public sealed class SubscriptionCheckoutFacadeDelegationTests
{
    [Fact]
    public async Task PersonalPaidCommandsDelegateWithUniqueOperationIdsAndMapPaymentRequired()
    {
        const string UserId = "billing-user";
        const int PlanId = 12;
        var buyer = new PaymentBuyerDto { TypeCode = "individual", Name = "Покупатель" };
        var operationIds = new List<Guid>();
        var checkout = new Mock<IPersonalSubscriptionCheckoutService>(MockBehavior.Strict);
        checkout.Setup(service => service.PurchaseAsync(UserId, PlanId, buyer, It.IsAny<Guid>(), "https://example.test/return"))
            .Returns((string _, int _, PaymentBuyerDto? _, Guid operationId, string? _) =>
            {
                operationIds.Add(operationId);
                return Task.FromResult(PaymentRequiredResult());
            });
        checkout.Setup(service => service.ExtendAsync(UserId, buyer, It.IsAny<Guid>(), "https://example.test/return"))
            .Returns((string _, PaymentBuyerDto? _, Guid operationId, string? _) =>
            {
                operationIds.Add(operationId);
                return Task.FromResult(PaymentRequiredResult());
            });
        var facade = new SubscriptionService(
            Mock.Of<IVehicleQueryService>(),
            Mock.Of<IPersonalSubscriptionCommandService>(),
            checkout.Object,
            Mock.Of<IPersonalSubscriptionQueryService>(),
            Mock.Of<IPersonalSubscriptionCatalogQueryService>(),
            Mock.Of<IPersonalSubscriptionActivationService>(),
            Mock.Of<IPersonalSubscriptionPurchasePolicyService>());

        var purchase = await facade.PurchaseSubscriptionAsync(
            UserId, PlanId, returnUrl: "https://example.test/return", buyer: buyer);
        var extension = await facade.ExtendSubscriptionAsync(UserId, "https://example.test/return", buyer);

        Assert.True(purchase.Success);
        Assert.True(purchase.RequiresPayment);
        Assert.Equal("payment-id", purchase.PaymentId);
        Assert.Equal("https://example.test/payment", purchase.PaymentUrl);
        Assert.True(extension.Success);
        Assert.True(extension.RequiresPayment);
        Assert.Equal("payment-id", extension.PaymentId);
        Assert.Equal(2, operationIds.Count);
        Assert.All(operationIds, operationId => Assert.NotEqual(Guid.Empty, operationId));
        Assert.NotEqual(operationIds[0], operationIds[1]);
        checkout.VerifyAll();
    }

    [Fact]
    public async Task OrganizationPaidCommandsDelegateWithUniqueOperationIdsAndMapPaymentRequired()
    {
        const string UserId = "billing-user";
        var organizationId = Guid.NewGuid();
        const int PlanId = 12;
        var buyer = new PaymentBuyerDto { TypeCode = "business", Name = "Покупатель", Inn = "1234567890" };
        var operationIds = new List<Guid>();
        var checkout = new Mock<IOrganizationSubscriptionCheckoutService>(MockBehavior.Strict);
        checkout.Setup(service => service.PurchaseAsync(UserId, organizationId, PlanId, buyer, It.IsAny<Guid>(), null))
            .Returns((string _, Guid _, int _, PaymentBuyerDto? _, Guid operationId, string? _) =>
            {
                operationIds.Add(operationId);
                return Task.FromResult(PaymentRequiredResult());
            });
        checkout.Setup(service => service.ExtendAsync(UserId, organizationId, buyer, It.IsAny<Guid>(), null))
            .Returns((string _, Guid _, PaymentBuyerDto? _, Guid operationId, string? _) =>
            {
                operationIds.Add(operationId);
                return Task.FromResult(PaymentRequiredResult());
            });
        var facade = new OrganizationSubscriptionService(
            Mock.Of<IOrganizationSubscriptionCatalogQueryService>(),
            Mock.Of<IOrganizationEntitlementService>(),
            Mock.Of<IWorkspaceAccessService>(),
            checkout.Object,
            Mock.Of<IOrganizationSubscriptionExtensionQueryService>(),
            Mock.Of<IOrganizationSubscriptionCommandService>(),
            Mock.Of<IOrganizationSubscriptionActivationService>(),
            Mock.Of<IOrganizationSubscriptionPurchasePolicyService>());

        var purchase = await facade.PurchaseSubscriptionAsync(UserId, organizationId, PlanId, buyer: buyer);
        var extension = await facade.ExtendSubscriptionAsync(UserId, organizationId, buyer);

        Assert.True(purchase.Success);
        Assert.True(purchase.RequiresPayment);
        Assert.Equal("payment-id", purchase.PaymentId);
        Assert.Equal("https://example.test/payment", purchase.PaymentUrl);
        Assert.True(extension.Success);
        Assert.True(extension.RequiresPayment);
        Assert.Equal("payment-id", extension.PaymentId);
        Assert.Equal(2, operationIds.Count);
        Assert.All(operationIds, operationId => Assert.NotEqual(Guid.Empty, operationId));
        Assert.NotEqual(operationIds[0], operationIds[1]);
        checkout.VerifyAll();
    }

    [Fact]
    public async Task InProgressCheckoutIsReturnedAsFailureByLegacyFacade()
    {
        const string UserId = "billing-user";
        const string Message = "Состояние платежа проверяется.";
        var checkout = new Mock<IPersonalSubscriptionCheckoutService>(MockBehavior.Strict);
        checkout.Setup(service => service.PurchaseAsync(UserId, 12, null, It.IsAny<Guid>(), null))
            .ReturnsAsync(new PaymentCreationResult(PaymentCreationStatus.InProgress, Message));
        var facade = new SubscriptionService(
            Mock.Of<IVehicleQueryService>(),
            Mock.Of<IPersonalSubscriptionCommandService>(),
            checkout.Object,
            Mock.Of<IPersonalSubscriptionQueryService>(),
            Mock.Of<IPersonalSubscriptionCatalogQueryService>(),
            Mock.Of<IPersonalSubscriptionActivationService>(),
            Mock.Of<IPersonalSubscriptionPurchasePolicyService>());

        var result = await facade.PurchaseSubscriptionAsync(UserId, 12);

        Assert.False(result.Success);
        Assert.False(result.RequiresPayment);
        Assert.Equal(Message, result.Error);
        checkout.VerifyAll();
    }

    private static PaymentCreationResult PaymentRequiredResult() => new(
        PaymentCreationStatus.PaymentRequired,
        PaymentId: "payment-id",
        PaymentUrl: "https://example.test/payment");
}
