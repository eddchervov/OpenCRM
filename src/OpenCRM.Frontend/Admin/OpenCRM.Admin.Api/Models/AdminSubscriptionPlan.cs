namespace OpenCRM.Admin.Api.Models;

/// <summary> Тариф для административного управления. </summary>
public sealed class AdminSubscriptionPlan
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public decimal? DiscountPrice { get; init; }
    public decimal EffectivePrice { get; init; }
    public bool HasDiscount { get; init; }
    public int DurationDays { get; init; }
    public bool IsActive { get; init; }
    public int VehicleLimit { get; init; }
    public int Order { get; init; }
    public string? Group { get; init; }
    public string PlanTypeCode { get; init; } = "personal";
    public string PlanTypeName { get; init; } = string.Empty;
    public bool IsDemoPlan { get; init; }
    public bool IsDefaultPlan { get; init; }
    public bool HasUsageHistory { get; init; }
    public bool CanDelete { get; init; }
}
