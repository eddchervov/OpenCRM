using Payments.Service.Domain;
using Payments.Service.Processing;

namespace Payments.Tests.Unit.Processing;

public sealed class PaymentReconciliationScheduleTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.WaitingForCapture)]
    public void SchedulesUnfinishedPaymentsEveryThirtySeconds(PaymentStatus status)
    {
        var order = CreateOrder(status, s_now.AddDays(-90));

        Assert.Equal(s_now.AddSeconds(30), PaymentReconciliationSchedule.GetNextCheckAt(order, s_now));
    }

    [Theory]
    [InlineData(23, 59, 15)]
    [InlineData(24, 0, 1440)]
    [InlineData(719, 59, 1440)]
    [InlineData(720, 0, 10080)]
    [InlineData(4000, 0, 10080)]
    public void SchedulesSuccessfulPaymentsByAge(int ageHours, int ageMinutes, int intervalMinutes)
    {
        var order = CreateOrder(PaymentStatus.Succeeded, s_now.AddHours(-ageHours).AddMinutes(-ageMinutes));

        Assert.Equal(s_now.AddMinutes(intervalMinutes), PaymentReconciliationSchedule.GetNextCheckAt(order, s_now));
    }

    [Fact]
    public void LatePaymentConfirmationStartsRecentReconciliationWindowAtSuccess()
    {
        var order = CreateOrder(PaymentStatus.Succeeded, s_now.AddDays(-45));
        order.SucceededAt = s_now;

        Assert.Equal(s_now.AddMinutes(15), PaymentReconciliationSchedule.GetNextCheckAt(order, s_now));
    }

    [Fact]
    public void MissingSuccessTimestampUsesCurrentTimeForSafeInitialWindow()
    {
        var order = CreateOrder(PaymentStatus.Succeeded, s_now.AddDays(-45));
        order.SucceededAt = null;

        Assert.Equal(s_now.AddMinutes(15), PaymentReconciliationSchedule.GetNextCheckAt(order, s_now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12499)]
    public void KeepsPartiallyRefundedPaymentsInControlReconciliation(long refundedAmountMinor)
    {
        var order = CreateOrder(PaymentStatus.Succeeded, s_now.AddDays(-90));
        order.RefundedAmountMinor = refundedAmountMinor;

        Assert.Equal(s_now.AddDays(7), PaymentReconciliationSchedule.GetNextCheckAt(order, s_now));
    }

    [Fact]
    public void StopsReconciliationAfterFullRefund()
    {
        var order = CreateOrder(PaymentStatus.Succeeded, s_now.AddDays(-90));
        order.RefundedAmountMinor = order.AmountMinor;

        Assert.Equal(DateTimeOffset.MaxValue, PaymentReconciliationSchedule.GetNextCheckAt(order, s_now));
    }

    [Fact]
    public void StopsReconciliationAfterCancellation()
    {
        var order = CreateOrder(PaymentStatus.Canceled, s_now);

        Assert.Equal(DateTimeOffset.MaxValue, PaymentReconciliationSchedule.GetNextCheckAt(order, s_now));
    }

    private static PaymentOrder CreateOrder(PaymentStatus status, DateTimeOffset createdAt) => new()
    {
        AmountMinor = 12500,
        Status = status,
        CreatedAt = createdAt,
        SucceededAt = status == PaymentStatus.Succeeded ? createdAt : null
    };
}
