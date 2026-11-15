namespace OpenCRM.Domain.Assistant;

public sealed class AssistantTelegramUpdate
{
    public long UpdateId { get; set; }
    public string Status { get; set; } = "pending";
    public string PayloadJson { get; set; } = "{}";
    public string? ResponseJson { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
