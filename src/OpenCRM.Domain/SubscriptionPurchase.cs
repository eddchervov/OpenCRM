namespace OpenCRM.Domain;

/// <summary> Покупка подписки с неизменяемыми условиями и состоянием применения оплаты. </summary>
public sealed class SubscriptionPurchase
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid? OrganizationId { get; set; }
    public string BeneficiaryKey { get; set; } = string.Empty;
    public bool IsOpen { get; set; } = true;
    public Guid CreationOperationId { get; set; }
    public PaymentCreationOperation CreationOperation { get; set; } = null!;
    public int SubscriptionPaymentId { get; set; }
    public SubscriptionPayment Payment { get; set; } = null!;
    public string PlanSnapshot { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "RUB";
    public Guid? ProviderOrderId { get; set; }
    public long ProviderVersion { get; set; }
    public string ProviderStatus { get; set; } = "NotCreated";
    public string CreationStatus { get; set; } = "Prepared";
    public long RefundedAmountMinor { get; set; }
    public bool IsTest { get; set; }
    public string ApplicationStatus { get; set; } = SubscriptionPurchaseStatuses.AwaitingPayment;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? AppliedAt { get; set; }
    public DateTime? GrantedFrom { get; set; }
    public DateTime? GrantedUntil { get; set; }
    public int ApplicationAttempts { get; set; }
    public DateTime NextApplicationAttemptAt { get; set; }
    public DateTime NextReconcileAt { get; set; }
    public string? LastErrorCode { get; set; }
}

/// <summary> Отдельные от денежных фактов состояния предоставления доступа. </summary>
public static class SubscriptionPurchaseStatuses
{
    public const string AwaitingPayment = "awaiting_payment";
    public const string ActivationPending = "activation_pending";
    public const string Applied = "applied";
    public const string NeedsAttention = "needs_attention";
    public const string Refunded = "refunded";
    public const string Closed = "closed";
}
