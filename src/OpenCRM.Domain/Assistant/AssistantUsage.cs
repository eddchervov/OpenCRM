namespace OpenCRM.Domain.Assistant;

public sealed class AssistantUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public Guid? ConversationId { get; set; }
    public string Status { get; set; } = "reserved";
    public string? ProviderRequestId { get; set; }
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public long? CachedTokens { get; set; }
    public decimal? AudioSeconds { get; set; }
    public int? InputCharacters { get; set; }
    public decimal ReservedCostRub { get; set; }
    public decimal? CostRub { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
