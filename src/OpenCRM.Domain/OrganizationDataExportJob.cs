namespace OpenCRM.Domain;

/// <summary> Сохранённое задание подготовки переносимого архива организации. </summary>
public sealed class OrganizationDataExportJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public string State { get; set; } = "queued";
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? StoragePath { get; set; }
    public long? ArchiveSizeBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? CleanupAfterUtc { get; set; }
}
