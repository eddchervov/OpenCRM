namespace Payments.Service.Providers;

public sealed record ProviderRefund(string Id, string PaymentId, string Status, long AmountMinor, string Currency);
