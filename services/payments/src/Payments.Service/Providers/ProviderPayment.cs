namespace Payments.Service.Providers;

public sealed record ProviderPayment(
    string Id,
    string Status,
    long AmountMinor,
    string Currency,
    bool Paid,
    bool IsTest,
    long RefundedAmountMinor,
    string? ConfirmationUrl,
    string PaymentOrderId,
    string MerchantAccountId);
