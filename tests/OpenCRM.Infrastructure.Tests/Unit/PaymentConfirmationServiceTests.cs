using Moq;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;
using OpenCRM.Infrastructure.Payments;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class PaymentConfirmationServiceTests
{
    [Fact]
    public async Task ReconciledWorkspacePaymentUsesNormalActivationPath()
    {
        var organizationId = Guid.NewGuid();
        var payment = new LocalPaymentInfoDto
        {
            UserId = "buyer",
            PlanId = 42,
            Amount = 100,
            PaymentStatus = "pending",
            PaymentMethod = $"yookassa_workspace_purchase:{organizationId}"
        };
        var yooKassa = new Mock<IYooKassaService>();
        yooKassa.Setup(service => service.GetLocalPaymentInfoAsync("payment"))
            .ReturnsAsync(payment);
        yooKassa.Setup(service => service.MarkPaymentPaidAsync("payment"))
            .ReturnsAsync(true);
        yooKassa.Setup(service => service.GetPaymentStatusAsync("payment"))
            .ReturnsAsync(new PaymentResponse
            {
                IsSuccess = true,
                PaymentId = "payment",
                Status = "succeeded",
                Paid = true,
                Currency = "RUB",
                Amount = 100
            });
        var organizations = new Mock<IOrganizationSubscriptionActivationService>();
        organizations.Setup(service => service.ActivateNewSubscriptionAsync("buyer", organizationId, 42, "payment"))
            .ReturnsAsync(SubscriptionResult.CreateSuccess());
        var personal = new Mock<IPersonalSubscriptionActivationService>();
        var service = new PaymentConfirmationService(organizations.Object, personal.Object, yooKassa.Object);

        Assert.True(await service.ConfirmAsync("payment"));
        organizations.Verify(service => service.ActivateNewSubscriptionAsync("buyer", organizationId, 42, "payment"), Times.Once);
        personal.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PaidActivationFailureIsRecordedForManualReview()
    {
        var payment = new LocalPaymentInfoDto
        {
            UserId = "buyer",
            PlanId = 42,
            Amount = 100,
            PaymentStatus = "pending",
            PaymentMethod = "yookassa_purchase"
        };
        var yooKassa = new Mock<IYooKassaService>();
        yooKassa.SetupSequence(service => service.GetLocalPaymentInfoAsync("payment"))
            .ReturnsAsync(payment)
            .ReturnsAsync(new LocalPaymentInfoDto { PaymentStatus = "failed" });
        yooKassa.Setup(service => service.MarkPaymentPaidAsync("payment"))
            .ReturnsAsync(true);
        yooKassa.Setup(service => service.GetPaymentStatusAsync("payment"))
            .ReturnsAsync(new PaymentResponse
            {
                IsSuccess = true,
                PaymentId = "payment",
                Status = "succeeded",
                Paid = true,
                Currency = "RUB",
                Amount = 100
            });
        var personal = new Mock<IPersonalSubscriptionActivationService>();
        personal.Setup(service => service.ActivateNewSubscriptionAsync("buyer", 42, "payment"))
            .ReturnsAsync(SubscriptionResult.CreateFailed("Тариф недоступен"));
        var service = new PaymentConfirmationService(
            new Mock<IOrganizationSubscriptionActivationService>().Object, personal.Object, yooKassa.Object);

        Assert.False(await service.ConfirmAsync("payment"));
        personal.Verify(service => service.ActivateNewSubscriptionAsync("buyer", 42, "payment"), Times.Once);
    }

    [Fact]
    public async Task RefundedPaymentIsNeverActivatedAgain()
    {
        var yooKassa = new Mock<IYooKassaService>();
        yooKassa.Setup(service => service.GetLocalPaymentInfoAsync("payment"))
            .ReturnsAsync(new LocalPaymentInfoDto { PaymentStatus = "refunded" });
        yooKassa.Setup(service => service.MarkPaymentPaidAsync("payment"))
            .ReturnsAsync(true);
        var organizations = new Mock<IOrganizationSubscriptionActivationService>();
        var personal = new Mock<IPersonalSubscriptionActivationService>();
        var service = new PaymentConfirmationService(organizations.Object, personal.Object, yooKassa.Object);

        Assert.True(await service.ConfirmAsync("payment"));
        organizations.VerifyNoOtherCalls();
        personal.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FullyRefundedPendingPaymentIsNotActivated()
    {
        var yooKassa = new Mock<IYooKassaService>();
        yooKassa.Setup(service => service.GetLocalPaymentInfoAsync("payment"))
            .ReturnsAsync(new LocalPaymentInfoDto { PaymentStatus = "pending", Amount = 100 });
        yooKassa.Setup(service => service.GetPaymentStatusAsync("payment"))
            .ReturnsAsync(new PaymentResponse
            {
                IsSuccess = true,
                PaymentId = "payment",
                Status = "succeeded",
                Paid = true,
                Currency = "RUB",
                Amount = 100,
                RefundedAmount = 100,
                RefundedCurrency = "RUB"
            });
        yooKassa.Setup(service => service.MarkPaymentPaidAsync("payment"))
            .ReturnsAsync(true);
        var organizations = new Mock<IOrganizationSubscriptionActivationService>();
        var personal = new Mock<IPersonalSubscriptionActivationService>();
        var service = new PaymentConfirmationService(organizations.Object, personal.Object, yooKassa.Object);

        Assert.True(await service.ConfirmAsync("payment"));
        organizations.VerifyNoOtherCalls();
        personal.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("yookassa_purchase")]
    [InlineData("yookassa_extension")]
    [InlineData("yookassa_purchase", "processing")]
    [InlineData("yookassa_extension", "processing")]
    public async Task ConfirmedPersonalPaymentCallsNarrowActivationPortOnce(string operation, string status = "pending")
    {
        var payments = PaymentConfirmationServiceTests.CreateConfirmedPayment(operation, status);
        var personal = new Mock<IPersonalSubscriptionActivationService>(MockBehavior.Strict);
        var organizations = new Mock<IOrganizationSubscriptionActivationService>(MockBehavior.Strict);
        if (operation == "yookassa_extension")
        {
            personal.Setup(service => service.ActivateExtendedSubscriptionAsync("buyer", 42, "payment"))
                .ReturnsAsync(SubscriptionResult.CreateSuccess());
        }
        else
        {
            personal.Setup(service => service.ActivateNewSubscriptionAsync("buyer", 42, "payment"))
                .ReturnsAsync(SubscriptionResult.CreateSuccess());
        }

        var service = new PaymentConfirmationService(organizations.Object, personal.Object, payments.Object);

        Assert.True(await service.ConfirmAsync("payment"));
        personal.VerifyAll();
        Assert.Single(personal.Invocations);
        organizations.VerifyNoOtherCalls();
        payments.Verify(service => service.MarkPaymentPaidAsync("payment"), Times.Once);
    }

    [Theory]
    [InlineData("completed", true)]
    [InlineData("failed", false)]
    public async Task TerminalPersonalPaymentDoesNotCallProviderOrActivation(string status, bool expected)
    {
        var payments = new Mock<IYooKassaService>(MockBehavior.Strict);
        payments.Setup(service => service.GetLocalPaymentInfoAsync("payment"))
            .ReturnsAsync(new LocalPaymentInfoDto { PaymentStatus = status });
        var personal = new Mock<IPersonalSubscriptionActivationService>(MockBehavior.Strict);
        var organizations = new Mock<IOrganizationSubscriptionActivationService>(MockBehavior.Strict);
        var service = new PaymentConfirmationService(organizations.Object, personal.Object, payments.Object);

        Assert.Equal(expected, await service.ConfirmAsync("payment"));
        payments.Verify(service => service.GetLocalPaymentInfoAsync("payment"), Times.Once);
        payments.VerifyNoOtherCalls();
        personal.VerifyNoOtherCalls();
        organizations.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnconfirmedProviderPaymentNeverReachesActivation()
    {
        var payments = PaymentConfirmationServiceTests.CreateConfirmedPayment("yookassa_purchase");
        payments.Setup(service => service.GetPaymentStatusAsync("payment"))
            .ReturnsAsync(new PaymentResponse { IsSuccess = true, PaymentId = "payment", Status = "pending" });
        var personal = new Mock<IPersonalSubscriptionActivationService>(MockBehavior.Strict);
        var service = new PaymentConfirmationService(
            new Mock<IOrganizationSubscriptionActivationService>(MockBehavior.Strict).Object, personal.Object, payments.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync("payment"));
        payments.Verify(service => service.MarkPaymentPaidAsync(It.IsAny<string>()), Times.Never);
        personal.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, "pending")]
    [InlineData(true, "pending")]
    [InlineData(false, "processing")]
    [InlineData(true, "processing")]
    public async Task ConfirmedWorkspacePaymentCallsNarrowActivationPortOnce(bool extend, string status)
    {
        var organizationId = Guid.NewGuid();
        var operation = $"yookassa_workspace_{(extend ? "extension" : "purchase")}:{organizationId:D}";
        var payments = PaymentConfirmationServiceTests.CreateConfirmedPayment(operation, status);
        var personal = new Mock<IPersonalSubscriptionActivationService>(MockBehavior.Strict);
        var organizations = new Mock<IOrganizationSubscriptionActivationService>(MockBehavior.Strict);
        if (extend)
        {
            organizations.Setup(service => service.ActivateExtendedSubscriptionAsync("buyer", organizationId, 42, "payment"))
                .ReturnsAsync(SubscriptionResult.CreateSuccess());
        }
        else
        {
            organizations.Setup(service => service.ActivateNewSubscriptionAsync("buyer", organizationId, 42, "payment"))
                .ReturnsAsync(SubscriptionResult.CreateSuccess());
        }

        var service = new PaymentConfirmationService(organizations.Object, personal.Object, payments.Object);

        Assert.True(await service.ConfirmAsync("payment"));
        organizations.VerifyAll();
        Assert.Single(organizations.Invocations);
        personal.VerifyNoOtherCalls();
        payments.Verify(service => service.MarkPaymentPaidAsync("payment"), Times.Once);
    }

    private static Mock<IYooKassaService> CreateConfirmedPayment(string operation, string status = "pending")
    {
        var payments = new Mock<IYooKassaService>(MockBehavior.Strict);
        payments.Setup(service => service.GetLocalPaymentInfoAsync("payment"))
            .ReturnsAsync(new LocalPaymentInfoDto
            {
                UserId = "buyer", PlanId = 42, Amount = 100,
                PaymentStatus = status, PaymentMethod = operation
            });
        payments.Setup(service => service.MarkPaymentPaidAsync("payment")).ReturnsAsync(true);
        payments.Setup(service => service.GetPaymentStatusAsync("payment"))
            .ReturnsAsync(new PaymentResponse
            {
                IsSuccess = true, PaymentId = "payment", Status = "succeeded",
                Paid = true, Currency = "RUB", Amount = 100
            });

        return payments;
    }
}
