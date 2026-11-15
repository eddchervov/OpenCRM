namespace OpenCRM.Domain;

/// <summary> Долговечное исходящее поручение платёжному сервису. </summary>
public sealed class SubscriptionPaymentCommand
{
    public Guid Id { get; set; }
    public Guid PurchaseId { get; set; }
    public SubscriptionPurchase Purchase { get; set; } = null!;
    public string Kind { get; set; } = "create";
    public string Payload { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public int Attempts { get; set; }
    public string? LastErrorCode { get; set; }
}
