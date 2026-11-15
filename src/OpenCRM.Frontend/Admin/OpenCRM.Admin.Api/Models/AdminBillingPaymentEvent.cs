namespace OpenCRM.Admin.Api.Models;

/// <summary> Безопасное событие обработки покупки в платёжном сервисе. </summary>
public sealed class AdminBillingPaymentEvent
{
    public Guid Id { get; init; }
    public Guid? MessageId { get; init; }
    public Guid? PurchaseId { get; init; }
    public Guid? CommandId { get; init; }
    public Guid? ProviderOrderId { get; init; }
    public long? ProviderVersion { get; init; }
    public string Stage { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? ProcessedAt { get; init; }
    public long? AmountMinor { get; init; }
    public long? RefundedAmountMinor { get; init; }
    public string Currency { get; init; } = string.Empty;
}
