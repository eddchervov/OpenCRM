namespace Payments.Contracts;

/// <summary> Передаёт полное проверенное состояние оплаты; версия защищает от перестановки событий. </summary>
public sealed record PaymentStateChanged(
    Guid EventId,
    string ApplicationId,
    Guid PurchaseId,
    Guid PaymentOrderId,
    long AggregateVersion,
    string CreationStatus,
    string PaymentStatus,
    long AmountMinor,
    long RefundedAmountMinor,
    string Currency,
    string? ProviderPaymentId,
    string? ConfirmationUrl,
    DateTimeOffset OccurredAt,
    bool IsTest,
    int SchemaVersion = 1);
