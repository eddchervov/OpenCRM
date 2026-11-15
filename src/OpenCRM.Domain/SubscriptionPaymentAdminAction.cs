namespace OpenCRM.Domain;

/// <summary> Долговечное поручение администратора и его результат; денежные факты не заменяет. </summary>
public sealed class SubscriptionPaymentAdminAction
{
    public Guid Id { get; set; }
    public Guid PurchaseId { get; set; }
    public SubscriptionPurchase Purchase { get; set; } = null!;
    public string Action { get; set; } = string.Empty;
    public string ActorUserId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long? AmountMinor { get; set; }
    public long RefundedBeforeMinor { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string StateToken { get; set; } = string.Empty;
    public string? VerifiedStateToken { get; set; }
    public string Status { get; set; } = "queued";
    public string? Code { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? ProviderCheckRequestedAt { get; set; }
}
