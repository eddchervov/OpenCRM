using Payments.Service.Domain;

namespace Payments.Service.Processing;

public static class PaymentReconciliationSchedule
{
    private static readonly TimeSpan s_pendingInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_recentSucceededInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan s_dailySucceededInterval = TimeSpan.FromDays(1);
    private static readonly TimeSpan s_weeklySucceededInterval = TimeSpan.FromDays(7);
    private static readonly TimeSpan s_recentSucceededWindow = TimeSpan.FromDays(1);
    private static readonly TimeSpan s_dailySucceededWindow = TimeSpan.FromDays(30);

    public static DateTimeOffset GetNextCheckAt(PaymentOrder order, DateTimeOffset now)
    {
        if (order.Status == PaymentStatus.Canceled
            || order.AmountMinor > 0 && order.RefundedAmountMinor >= order.AmountMinor)
        {
            return DateTimeOffset.MaxValue;
        }

        if (order.Status is PaymentStatus.Pending or PaymentStatus.WaitingForCapture)
        {
            return now.Add(s_pendingInterval);
        }

        var succeededAt = order.SucceededAt ?? now;
        var age = now - succeededAt;
        var interval = age < s_recentSucceededWindow
            ? s_recentSucceededInterval
            : age < s_dailySucceededWindow
                ? s_dailySucceededInterval
                : s_weeklySucceededInterval;

        return now.Add(interval);
    }
}
