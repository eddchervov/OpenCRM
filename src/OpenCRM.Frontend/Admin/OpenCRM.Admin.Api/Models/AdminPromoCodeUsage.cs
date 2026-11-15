namespace OpenCRM.Admin.Api.Models;

/// <summary> История применения промокода. </summary>
public sealed class AdminPromoCodeUsage
{
    public long Id { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public Guid? OrganizationId { get; init; }
    public string? OrganizationName { get; init; }
    public DateTime RedeemedAt { get; init; }
    public DateTime? AssignedAt { get; init; }
    public DateTime? DemoAppliedAt { get; init; }
    public string? ApplicationStatus { get; init; }
    public DateTime? DiscountValidUntil { get; init; }
    public int? SubscriptionPaymentId { get; init; }
    public string SourceCode { get; init; } = string.Empty;
}
