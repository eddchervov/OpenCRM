namespace OpenCRM.Admin.Api.Models;

/// <summary> Параметры создания или изменения промокода. </summary>
public sealed record AdminPromoCodeSaveRequest(
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    DateTime? ValidFrom,
    DateTime? ValidUntil,
    int? MaximumRedemptions,
    int? DemoDurationDays,
    string DiscountTypeCode,
    decimal? DiscountValue,
    int? DiscountValidityDays,
    int? DiscountPaymentLimitPerUser,
    bool AppliesToAllPaidPlans,
    IReadOnlyList<int> SubscriptionPlanIds,
    int? BonusDays,
    int? MaximumOrganizationApplications,
    bool AppliesToAllScenarios,
    IReadOnlyList<string> ScenarioCodes,
    int? GiftTemplatePlanId);
