namespace OpenCRM.Domain;

/// <summary> Редактируемая строка подготовки и журнал её применения. </summary>
public sealed class OrganizationImportRow
{
    public Guid DraftId { get; set; }
    public string Id { get; set; } = string.Empty;
    public int Position { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SearchText { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? ParentExternalId { get; set; }
    public Guid? TargetId { get; set; }
    public DateTime? AppliedAt { get; set; }
}
