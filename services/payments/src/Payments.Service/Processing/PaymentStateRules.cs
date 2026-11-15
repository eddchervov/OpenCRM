using Payments.Service.Domain;
using Payments.Service.Providers;

namespace Payments.Service.Processing;

public static class PaymentStateRules
{
    public static PaymentStatus ParseStatus(string status) => status switch
    {
        "pending" => PaymentStatus.Pending,
        "waiting_for_capture" => PaymentStatus.WaitingForCapture,
        "succeeded" => PaymentStatus.Succeeded,
        "canceled" => PaymentStatus.Canceled,
        _ => throw new PaymentProviderException("payment_status_unknown", requiresManualReview: true)
    };

    public static bool IsValid(ProviderPayment payment, PaymentOrder order, bool? expectedIsTest = null) =>
        payment.Id.Length is > 0 and <= 128
        && payment.AmountMinor == order.AmountMinor
        && string.Equals(payment.Currency, order.Currency, StringComparison.Ordinal)
        && payment.IsTest == order.IsTest
        && (!expectedIsTest.HasValue || payment.IsTest == expectedIsTest.Value)
        && string.Equals(payment.MerchantAccountId, order.MerchantAccountId, StringComparison.Ordinal)
        && Guid.TryParse(payment.PaymentOrderId, out var paymentOrderId)
        && paymentOrderId == order.Id
        && payment.RefundedAmountMinor is >= 0
        && payment.RefundedAmountMinor <= order.AmountMinor
        && IsValidStatus(payment);

    public static bool IsValidStatus(ProviderPayment payment) => payment.Status switch
    {
        "pending" => !payment.Paid,
        "waiting_for_capture" => payment.Paid,
        "succeeded" => payment.Paid,
        "canceled" => !payment.Paid,
        _ => false
    };

    public static bool CanApply(PaymentStatus current, PaymentStatus next) =>
        current == next
        || current == PaymentStatus.NotCreated
        || current == PaymentStatus.Pending && next != PaymentStatus.NotCreated
        || current == PaymentStatus.WaitingForCapture && next is (PaymentStatus.Succeeded or PaymentStatus.Canceled);
}
