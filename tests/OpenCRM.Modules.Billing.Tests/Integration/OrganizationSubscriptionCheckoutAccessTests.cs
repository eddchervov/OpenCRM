using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Services.Interfaces.Promotions;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class OrganizationSubscriptionCheckoutAccessTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task NonMemberCannotResumeOrCreateAnOrganizationPayment()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var gateway = new Mock<IPaymentCreationGateway>(MockBehavior.Strict);
        var promotions = new Mock<IPromoCodeService>(MockBehavior.Strict);
        using var provider = fixture.CreateServices(
            context,
            fixture.CreateDefaultsInitializer().Object,
            promoCodeService: promotions.Object,
            paymentCreationGateway: gateway.Object);
        using var scope = provider.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCheckoutService>();

        var result = await checkout.PurchaseAsync("another-user", organization.Id, 27, null, Guid.NewGuid());
        var extensionResult = await checkout.ExtendAsync("another-user", organization.Id, null, Guid.NewGuid());

        Assert.Equal(PaymentCreationStatus.Invalid, result.Status);
        Assert.Equal(PaymentCreationStatus.Invalid, extensionResult.Status);
        gateway.VerifyNoOtherCalls();
        promotions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingBillingPermissionIsRethrownBeforePaymentResume()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var permissions = new Mock<IOrganizationPermissionService>(MockBehavior.Strict);
        permissions.Setup(service => service.HasPermissionAsync(
                organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.BillingManage))
            .ReturnsAsync(false);
        var gateway = new Mock<IPaymentCreationGateway>(MockBehavior.Strict);
        using var provider = fixture.CreateServices(
            context,
            fixture.CreateDefaultsInitializer().Object,
            organizationPermissionService: permissions.Object,
            paymentCreationGateway: gateway.Object);
        using var scope = provider.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCheckoutService>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => checkout.PurchaseAsync(
            organization.OwnerUserId, organization.Id, 27, new PaymentBuyerDto { TypeCode = "individual" }, Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => checkout.ExtendAsync(
            organization.OwnerUserId, organization.Id, new PaymentBuyerDto { TypeCode = "individual" }, Guid.NewGuid()));

        permissions.Verify(service => service.HasPermissionAsync(
            organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.BillingManage), Times.Exactly(2));
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnknownGatewayFailuresLeavePurchaseAndExtensionInProgress()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var gateway = new Mock<IPaymentCreationGateway>(MockBehavior.Strict);
        gateway.Setup(service => service.ResumeAsync(It.IsAny<PaymentCreationIntent>()))
            .ThrowsAsync(new IOException("Временная ошибка хранилища."));
        using var provider = fixture.CreateServices(
            context,
            fixture.CreateDefaultsInitializer().Object,
            paymentCreationGateway: gateway.Object);
        using var scope = provider.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCheckoutService>();

        var purchase = await checkout.PurchaseAsync(organization.OwnerUserId, organization.Id, 27, null, Guid.NewGuid());
        var extension = await checkout.ExtendAsync(organization.OwnerUserId, organization.Id, null, Guid.NewGuid());

        Assert.Equal(PaymentCreationStatus.InProgress, purchase.Status);
        Assert.Equal(PaymentCreationStatus.InProgress, extension.Status);
        gateway.Verify(service => service.ResumeAsync(It.IsAny<PaymentCreationIntent>()), Times.Exactly(2));
    }
}
