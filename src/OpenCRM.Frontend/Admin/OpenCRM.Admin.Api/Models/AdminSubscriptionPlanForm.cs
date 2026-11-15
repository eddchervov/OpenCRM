namespace OpenCRM.Admin.Api.Models;

/// <summary> Редактируемые поля тарифа. </summary>
public sealed class AdminSubscriptionPlanForm
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? DiscountPrice { get; set; }
    public int DurationDays { get; set; } = 30;
    public int VehicleLimit { get; set; }
    public int Order { get; set; }
    public string? Group { get; set; }
    public string PlanTypeCode { get; set; } = "business";
    public bool IsActive { get; set; } = true;
    public bool IsDefaultPlan { get; set; }
    public bool IsDemoPlan { get; set; }
}
