namespace Payments.Service.Domain;

/// <summary> Сохраняет неизменяемый факт обработки денег или ошибку операции. </summary>
public sealed class PaymentAuditEntry
{
    public Guid Id { get; set; }
    public Guid? PaymentOrderId { get; set; }
    public Guid? ProviderOperationId { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? ErrorType { get; set; }
    public long AggregateVersion { get; set; }
    public long AmountMinor { get; set; }
    public long RefundedAmountMinor { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
