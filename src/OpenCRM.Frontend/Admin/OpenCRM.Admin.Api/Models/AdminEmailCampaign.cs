namespace OpenCRM.Admin.Api.Models;

public sealed class AdminEmailCampaign
{
    public Guid Id { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string StatusCode { get; init; } = string.Empty;
    public string TargetMode { get; init; } = string.Empty;
    public string? TargetOrganizationTypeCode { get; init; }
    public int IntervalSeconds { get; init; }
    public DateTime StartAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int TotalCount { get; init; }
    public int SentCount { get; init; }
    public int SimulatedCount { get; init; }
    public int FailedCount { get; init; }
    public int SkippedCount { get; init; }
    public string? LastError { get; init; }
}
