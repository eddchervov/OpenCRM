namespace OpenCRM.Admin.Api.Models;

/// <summary> Промокод административной панели. </summary>
public sealed class AdminPromoCode
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public DateTime? ValidFrom { get; init; }
    public DateTime? ValidUntil { get; init; }
    public int? MaximumRedemptions { get; init; }
    public int RedemptionCount { get; init; }
    public int? RemainingRedemptions { get; init; }
    public int? DemoDurationDays { get; init; }
    public string DiscountTypeCode { get; init; } = string.Empty;
    public decimal? DiscountValue { get; init; }
    public int? DiscountValidityDays { get; init; }
    public int? DiscountPaymentLimitPerUser { get; init; }
    public bool AppliesToAllPaidPlans { get; init; }
    public List<int> SubscriptionPlanIds { get; init; } = [];
    public List<string> SubscriptionPlanNames { get; init; } = [];
    public int? BonusDays { get; init; }
    public int? MaximumOrganizationApplications { get; init; }
    public int OrganizationApplicationCount { get; init; }
    public int? RemainingOrganizationApplications { get; init; }
    public bool AppliesToAllScenarios { get; init; }
    public List<string> ScenarioCodes { get; init; } = [];
    public int? GiftTemplatePlanId { get; init; }
    public bool IsOrganizationCode { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
