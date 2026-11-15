namespace OpenCRM.Domain;

public sealed class MaxClientNotification
{
    public long Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ClientId { get; set; }
    public long MaxUserId { get; set; }
    public Guid MaintenanceId { get; set; }
    public uint MaintenanceVersion { get; set; }
    public DateTime? VisitStartsAtUtc { get; set; }
    public string EventCode { get; set; } = string.Empty;
    public string NotificationKey { get; set; } = string.Empty;
    public Guid? NotificationCommandId { get; set; }
    public Guid? NotificationId { get; set; }
    public DateTime? NotificationDispatchedAt { get; set; }
    public DateTime? NotificationEventAt { get; set; }
    public string Body { get; set; } = string.Empty;
    public string Status { get; set; } = "queued";
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorCode { get; set; }
}
