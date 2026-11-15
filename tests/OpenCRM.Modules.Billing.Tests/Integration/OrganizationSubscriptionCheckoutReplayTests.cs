using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Services.Interfaces.Promotions;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class OrganizationSubscriptionCheckoutReplayTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PurchaseAndExtensionResumeBeforeCurrentPlanAndPromotionChecks()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var operationId = Guid.NewGuid();
        var buyer = new PaymentBuyerDto { TypeCode = "business", Name = "Покупатель", Inn = "1234567890" };
        var purchaseResult = new PaymentCreationResult(PaymentCreationStatus.InProgress, "Платеж проверяется.");
        var extensionResult = new PaymentCreationResult(PaymentCreationStatus.PaymentRequired, PaymentId: "saved-payment");
        var gateway = new Mock<IPaymentCreationGateway>(MockBehavior.Strict);
        gateway.Setup(service => service.ResumeAsync(It.Is<PaymentCreationIntent>(intent =>
                intent.OperationId == operationId
                && intent.UserId == organization.OwnerUserId
                && intent.OperationType == $"yookassa_workspace_purchase:{organization.Id:D}"
                && intent.SubscriptionPlanId == 27
                && intent.Buyer == buyer)))
            .ReturnsAsync(purchaseResult);
        gateway.Setup(service => service.ResumeAsync(It.Is<PaymentCreationIntent>(intent =>
                intent.OperationId == operationId
                && intent.UserId == organization.OwnerUserId
                && intent.OperationType == $"yookassa_workspace_extension:{organization.Id:D}"
                && intent.SubscriptionPlanId == null
                && intent.Buyer == buyer)))
            .ReturnsAsync(extensionResult);
        var promotions = new Mock<IPromoCodeService>(MockBehavior.Strict);

        using var provider = fixture.CreateServices(
            context,
            fixture.CreateDefaultsInitializer().Object,
            promoCodeService: promotions.Object,
            paymentCreationGateway: gateway.Object);
        using var scope = provider.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCheckoutService>();

        var purchase = await checkout.PurchaseAsync(
            organization.OwnerUserId, organization.Id, 27, buyer, operationId);
        var extension = await checkout.ExtendAsync(
            organization.OwnerUserId, organization.Id, buyer, operationId);

        Assert.Same(purchaseResult, purchase);
        Assert.Same(extensionResult, extension);
        gateway.VerifyAll();
        promotions.VerifyNoOtherCalls();
    }
}
