namespace OpenCRM.Admin.Api.Models;

public sealed class AdminCatalogSuggestion
{
    public int Id { get; init; }
    public int Kind { get; init; }
    public string KindText { get; init; } = string.Empty;
    public string BrandName { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public int Status { get; init; }
    public string StatusText { get; init; } = string.Empty;
    public string SuggestedByUserId { get; init; } = string.Empty;
    public string? SuggestedByEmail { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public string? ReviewedByUserId { get; init; }
    public string? AdminComment { get; init; }
    public int? ApprovedBrandId { get; init; }
    public int? ApprovedModelId { get; init; }
}
