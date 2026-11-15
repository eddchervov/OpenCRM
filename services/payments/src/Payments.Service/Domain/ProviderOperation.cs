namespace Payments.Service.Domain;

/// <summary> Сохраняет ключ и тело до внешнего запроса; неизвестный исход не разрешает новую операцию. </summary>
public sealed class ProviderOperation
{
    public Guid Id { get; set; }
    public Guid PaymentOrderId { get; set; }
    public PaymentOrder PaymentOrder { get; set; } = null!;
    public Guid CommandId { get; set; }
    public OperationKind Kind { get; set; }
    public string ExecutionMode { get; set; } = "Disabled";
    public long AmountMinor { get; set; }
    public string RequestBody { get; set; } = string.Empty;
    public string IdempotenceKey { get; set; } = string.Empty;
    public OperationStatus Status { get; set; }
    public string? ProviderObjectId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? Reason { get; set; }
    public string? RequestedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? FirstAttemptAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? PostBefore { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public Guid? LeaseToken { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}
