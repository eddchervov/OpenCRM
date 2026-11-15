namespace OpenCRM.Domain;

/// <summary> Неизменяемый журнал входящих финансовых фактов и ошибок применения. </summary>
public sealed class SubscriptionPaymentEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? MessageId { get; set; }
    public Guid? PurchaseId { get; set; }
    public SubscriptionPurchase? Purchase { get; set; }
    public Guid? CommandId { get; set; }
    public Guid? ProviderOrderId { get; set; }
    public long? ProviderVersion { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public int Attempts { get; set; }
    public string? MessageHash { get; set; }
    public long? AmountMinor { get; set; }
    public long? RefundedAmountMinor { get; set; }
    public DateTime CreatedAt { get; set; }
}
