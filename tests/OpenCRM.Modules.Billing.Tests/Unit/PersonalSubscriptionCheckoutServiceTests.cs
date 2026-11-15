using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Modules.Billing.Tests.Unit;

public sealed class PersonalSubscriptionCheckoutServiceTests
{
    [Fact]
    public async Task PurchaseResumesExistingOperationBeforeCheckingCurrentPlanRules()
    {
        await using var context = CreateContext();
        const string UserId = "billing-user";
        const int PlanId = 23;
        var operationId = Guid.NewGuid();
        var buyer = new PaymentBuyerDto { TypeCode = "individual", Name = "Покупатель" };
        var savedResult = new PaymentCreationResult(PaymentCreationStatus.InProgress, "Платеж уже создан.");
        var gateway = new Mock<IPaymentCreationGateway>(MockBehavior.Strict);
        gateway.Setup(service => service.ResumeAsync(It.Is<PaymentCreationIntent>(intent =>
                intent.OperationId == operationId
                && intent.UserId == UserId
                && intent.OperationType == "yookassa_purchase"
                && intent.SubscriptionPlanId == PlanId
                && intent.Buyer == buyer
                && intent.ReturnUrl == "https://example.test/return")))
            .ReturnsAsync(savedResult);
        var policy = new Mock<IPersonalSubscriptionPurchasePolicyService>(MockBehavior.Strict);

        var checkout = new PersonalSubscriptionCheckoutService(
            context,
            gateway.Object,
            policy.Object,
            Mock.Of<IPersonalSubscriptionQueryService>(),
            NullLogger<PersonalSubscriptionCheckoutService>.Instance);

        var result = await checkout.PurchaseAsync(UserId, PlanId, buyer, operationId, "https://example.test/return");

        Assert.Same(savedResult, result);
        gateway.VerifyAll();
        policy.VerifyNoOtherCalls();
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task UnknownResumeFailureReturnsInProgressSoTheClientKeepsTheSameOperationId()
    {
        await using var context = CreateContext();
        var gateway = new Mock<IPaymentCreationGateway>(MockBehavior.Strict);
        gateway.Setup(service => service.ResumeAsync(It.IsAny<PaymentCreationIntent>()))
            .ThrowsAsync(new IOException("Временная ошибка хранилища."));
        var checkout = new PersonalSubscriptionCheckoutService(
            context,
            gateway.Object,
            Mock.Of<IPersonalSubscriptionPurchasePolicyService>(MockBehavior.Strict),
            Mock.Of<IPersonalSubscriptionQueryService>(),
            NullLogger<PersonalSubscriptionCheckoutService>.Instance);

        var result = await checkout.PurchaseAsync("billing-user", 23, null, Guid.NewGuid());

        Assert.Equal(PaymentCreationStatus.InProgress, result.Status);
        Assert.Contains("тем же ключом", result.Message, StringComparison.Ordinal);
        gateway.VerifyAll();
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ExtensionResumesByStableOperationIdWithoutPinningTheCurrentPlan()
    {
        await using var context = CreateContext();
        const string UserId = "billing-user";
        var operationId = Guid.NewGuid();
        var buyer = new PaymentBuyerDto { TypeCode = "business", Name = "Покупатель" };
        var savedResult = new PaymentCreationResult(PaymentCreationStatus.PaymentRequired, PaymentId: "old-payment");
        var gateway = new Mock<IPaymentCreationGateway>(MockBehavior.Strict);
        gateway.Setup(service => service.ResumeAsync(It.Is<PaymentCreationIntent>(intent =>
                intent.OperationId == operationId
                && intent.UserId == UserId
                && intent.OperationType == "yookassa_extension"
                && intent.SubscriptionPlanId == null
                && intent.Buyer == buyer)))
            .ReturnsAsync(savedResult);
        var checkout = new PersonalSubscriptionCheckoutService(
            context,
            gateway.Object,
            Mock.Of<IPersonalSubscriptionPurchasePolicyService>(),
            Mock.Of<IPersonalSubscriptionQueryService>(),
            NullLogger<PersonalSubscriptionCheckoutService>.Instance);

        var result = await checkout.ExtendAsync(UserId, buyer, operationId);

        Assert.Same(savedResult, result);
        gateway.VerifyAll();
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task UnknownExtensionResumeFailureReturnsInProgress()
    {
        await using var context = CreateContext();
        var gateway = new Mock<IPaymentCreationGateway>(MockBehavior.Strict);
        gateway.Setup(service => service.ResumeAsync(It.IsAny<PaymentCreationIntent>()))
            .ThrowsAsync(new IOException("Временная ошибка хранилища."));
        var checkout = new PersonalSubscriptionCheckoutService(
            context,
            gateway.Object,
            Mock.Of<IPersonalSubscriptionPurchasePolicyService>(MockBehavior.Strict),
            Mock.Of<IPersonalSubscriptionQueryService>(),
            NullLogger<PersonalSubscriptionCheckoutService>.Instance);

        var result = await checkout.ExtendAsync("billing-user", null, Guid.NewGuid());

        Assert.Equal(PaymentCreationStatus.InProgress, result.Status);
        Assert.Contains("тем же ключом", result.Message, StringComparison.Ordinal);
        gateway.VerifyAll();
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static OpenCRMContext CreateContext() => new(new DbContextOptionsBuilder<OpenCRMContext>()
        .UseNpgsql("Host=127.0.0.1;Database=billing_checkout;Username=test;Password=test")
        .Options);
}
