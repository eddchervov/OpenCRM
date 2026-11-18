namespace OpenCRM.Domain;

/// <summary>Учтённая оплата записи с сохранением отменённых операций.</summary>
public class MaintenancePayment
{
    /// <summary>Код валюты оплаты на момент создания.</summary>
    public string CurrencyCode { get; set; } = CurrencyCodes.Rub;
    public Guid Id { get; set; }
    public Guid MaintenanceId { get; set; }
    public Maintenance Maintenance { get; set; } = null!;
    public decimal Amount { get; set; }
    public string? MethodCode { get; set; }
    public Guid? PayerClientId { get; set; }
    public string? PayerName { get; set; }
    public string? PayerPartyTypeCode { get; set; }
    public string? PayerInn { get; set; }
    public string? PayerKpp { get; set; }
    public string? PayerLegalAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public bool IsOpeningBalance { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
    public string? CancellationReason { get; set; }
}
