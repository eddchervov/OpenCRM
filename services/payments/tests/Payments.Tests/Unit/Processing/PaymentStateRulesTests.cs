using Payments.Service.Domain;
using Payments.Service.Processing;
using Payments.Service.Providers;

namespace Payments.Tests.Unit.Processing;

public sealed class PaymentStateRulesTests
{
    [Fact]
    public void AcceptsProviderPaymentOnlyWhenMoneyAndDestinationMatch()
    {
        var order = new PaymentOrder
        {
            Id = Guid.NewGuid(),
            AmountMinor = 12500,
            Currency = "RUB",
            MerchantAccountId = "test-shop",
            IsTest = true
        };
        var payment = new ProviderPayment(
            "provider-123",
            "succeeded",
            12500,
            "RUB",
            true,
            true,
            0,
            null,
            order.Id.ToString(),
            "test-shop");

        Assert.True(PaymentStateRules.IsValid(payment, order));
        Assert.False(PaymentStateRules.IsValid(payment with { AmountMinor = 12499 }, order));
        Assert.False(PaymentStateRules.IsValid(payment with { Currency = "USD" }, order));
        Assert.False(PaymentStateRules.IsValid(payment with { IsTest = false }, order));
        Assert.False(PaymentStateRules.IsValid(payment with { MerchantAccountId = "other-shop" }, order));
        Assert.False(PaymentStateRules.IsValid(payment, order, expectedIsTest: false));
        Assert.False(PaymentStateRules.IsValid(payment with { PaymentOrderId = Guid.NewGuid().ToString() }, order));
    }

    [Fact]
    public void RequiresProviderPaymentStatusToAgreeWithPaidFlag()
    {
        Assert.True(PaymentStateRules.IsValidStatus(CreatePayment("pending", paid: false)));
        Assert.True(PaymentStateRules.IsValidStatus(CreatePayment("succeeded", paid: true)));
        Assert.True(PaymentStateRules.IsValidStatus(CreatePayment("canceled", paid: false)));
        Assert.False(PaymentStateRules.IsValidStatus(CreatePayment("succeeded", paid: false)));
        Assert.False(PaymentStateRules.IsValidStatus(CreatePayment("pending", paid: true)));
        Assert.False(PaymentStateRules.IsValidStatus(CreatePayment("unknown", paid: false)));
    }

    [Fact]
    public void DoesNotRegressTerminalPaymentsOrAcceptCaptureInAutomaticCheckout()
    {
        Assert.True(PaymentStateRules.CanApply(PaymentStatus.Pending, PaymentStatus.Succeeded));
        Assert.True(PaymentStateRules.CanApply(PaymentStatus.Succeeded, PaymentStatus.Succeeded));
        Assert.False(PaymentStateRules.CanApply(PaymentStatus.Succeeded, PaymentStatus.Pending));
        Assert.False(PaymentStateRules.CanApply(PaymentStatus.Succeeded, PaymentStatus.Canceled));
        Assert.False(PaymentStateRules.CanApply(PaymentStatus.Canceled, PaymentStatus.Succeeded));
        Assert.True(PaymentStateRules.CanApply(PaymentStatus.Canceled, PaymentStatus.Canceled));
        Assert.Equal(PaymentStatus.WaitingForCapture, PaymentStateRules.ParseStatus("waiting_for_capture"));
    }

    [Fact]
    public void RejectsNegativeOrExcessiveRefundAmounts()
    {
        var order = new PaymentOrder { Id = Guid.NewGuid(), AmountMinor = 5000, RefundedAmountMinor = 5001 };
        var payment = new ProviderPayment(
            "provider-123",
            "succeeded",
            5000,
            "RUB",
            true,
            true,
            0,
            null,
            order.Id.ToString(),
            "shop");

        Assert.False(PaymentStateRules.IsValid(payment, order));
        Assert.False(PaymentStateRules.IsValid(payment with { RefundedAmountMinor = -1 }, order));
    }

    private static ProviderPayment CreatePayment(string status, bool paid) =>
        new("provider-123", status, 100, "RUB", paid, true, 0, null, Guid.NewGuid().ToString(), "shop");
}
