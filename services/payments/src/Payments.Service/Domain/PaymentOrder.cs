namespace Payments.Service.Domain;

/// <summary> Хранит неизменяемое поручение и последнее проверенное состояние денег. </summary>
public sealed class PaymentOrder
{
    public Guid Id { get; set; }
    public string ApplicationId { get; set; } = string.Empty;
    public Guid PurchaseId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "RUB";
    public string Description { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string BuyerId { get; set; } = string.Empty;
    public string BeneficiaryId { get; set; } = string.Empty;
    public string MerchantAccountId { get; set; } = string.Empty;
    public bool IsTest { get; set; } = true;
    public string? ProviderPaymentId { get; set; }
    public string? ConfirmationUrl { get; set; }
    public PaymentStatus Status { get; set; }
    public long RefundedAmountMinor { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SucceededAt { get; set; }
    public DateTimeOffset NextCheckAt { get; set; }
    public DateTimeOffset? LastReconciledAt { get; set; }
    public int ReconciliationFailureCount { get; set; }
    public string? LastError { get; set; }
    public List<ProviderOperation> Operations { get; set; } = [];
}
