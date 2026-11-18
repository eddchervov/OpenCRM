using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Изменяемые поля тарифа. </summary>
public partial class SubscriptionPlanEditor : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private decimal _price;

    [ObservableProperty]
    private decimal? _discountPrice;

    [ObservableProperty]
    private int _durationDays = 30;

    [ObservableProperty]
    private int _vehicleLimit;

    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private string? _group;

    [ObservableProperty]
    private string _planTypeCode = "business";

    [ObservableProperty]
    private bool _isActive = true;

    [ObservableProperty]
    private bool _isDefaultPlan;

    [ObservableProperty]
    private bool _isDemoPlan;

    [ObservableProperty]
    private bool _isDefaultPlanLocked;

    [ObservableProperty]
    private bool _isSystemFree;

    public void RefreshLocalizedValues() => this.OnPropertyChanged((string?)null);

    /// <summary> Копирует значения тарифа в форму. </summary>
    public void Load(AdminSubscriptionPlan plan)
    {
        this.Name = plan.Name;
        this.Description = plan.Description;
        this.Price = plan.Price;
        this.DiscountPrice = plan.DiscountPrice;
        this.DurationDays = plan.DurationDays;
        this.VehicleLimit = plan.VehicleLimit;
        this.Order = plan.Order;
        this.Group = plan.Group;
        this.PlanTypeCode = plan.PlanTypeCode;
        this.IsActive = plan.IsActive;
        this.IsDefaultPlan = plan.IsDefaultPlan;
        this.IsDefaultPlanLocked = plan.IsDefaultPlan;
        this.IsDemoPlan = plan.IsDemoPlan;
        this.IsSystemFree = string.Equals(plan.Group, "workspace.free.v1", StringComparison.Ordinal);
    }

    /// <summary> Создаёт DTO с нормализованными текстовыми полями. </summary>
    public AdminSubscriptionPlanForm ToRequest() => new()
    {
        Name = this.Name.Trim(),
        Description = this.Description.Trim(),
        Price = this.IsSystemFree ? 0 : this.Price,
        DiscountPrice = this.IsSystemFree ? null : this.DiscountPrice is >= 0 && this.DiscountPrice < this.Price ? this.DiscountPrice : null,
        DurationDays = this.DurationDays,
        VehicleLimit = this.VehicleLimit,
        Order = this.Order,
        Group = this.IsSystemFree ? "workspace.free.v1" : string.IsNullOrWhiteSpace(this.Group) ? null : this.Group.Trim(),
        PlanTypeCode = "business",
        IsActive = this.IsSystemFree || this.IsActive || this.IsDefaultPlan || this.IsDemoPlan,
        IsDefaultPlan = !this.IsSystemFree && this.IsDefaultPlan,
        IsDemoPlan = !this.IsSystemFree && this.IsDemoPlan
    };
}

/// <summary> Строка списка тарифов с доступными действиями. </summary>
public sealed class SubscriptionPlanListItem(AdminSubscriptionPlan plan, bool canToggle) : ObservableObject
{
    public AdminSubscriptionPlan Plan { get; } = plan;

    public int Id => this.Plan.Id;

    public string Name => this.Plan.Name;

    public string GroupLabel => this.Plan.Group == "workspace.free.v1"
        ? AdminLocalization.Instance["native.plans.sistemnyy.tarif"]
        : string.IsNullOrWhiteSpace(this.Plan.Group) ? AdminLocalization.Instance["native.plans.bez.gruppy"] : this.Plan.Group;

    public string PriceLabel => this.Plan.Group == "workspace.free.v1"
        ? AdminLocalization.Instance["native.plans.besplatno"]
        : UserDisplayFormatting.Money(this.Plan.EffectivePrice);

    public string DurationLabel => this.Plan.Group == "workspace.free.v1" || this.Plan.IsDefaultPlan && this.Plan.DurationDays >= 36500
        ? AdminLocalization.Instance["native.plans.bessrochno"]
        : AdminLocalization.Instance.Format("native.plans.this.plan.durationdays.dney", this.Plan.DurationDays);

    public string StatusLabel => this.Plan.Group == "workspace.free.v1" ? "Free" : this.Plan.IsActive ? AdminLocalization.Instance["native.plans.aktiven"] : AdminLocalization.Instance["native.plans.otklyuchyon"];

    public bool IsActive => this.Plan.IsActive;

    public bool CanToggle => canToggle && this.Plan.Group != "workspace.free.v1";

    public string ToggleLabel => this.IsActive ? AdminLocalization.Instance["native.plans.otklyuchit"] : AdminLocalization.Instance["native.plans.vklyuchit"];

    public string KindLabel => this.Plan switch
    {
        { Group: "workspace.free.v1" } => AdminLocalization.Instance["native.plans.sistemnyy.free"],
        { IsDefaultPlan: true } => AdminLocalization.Instance["native.plans.bazovyy.prezhnyaya.model"],
        { IsDemoPlan: true } => AdminLocalization.Instance["native.plans.demo.prezhnyaya.model"],
        { PlanTypeCode: "business" } => "PRO",
        _ => this.Plan.PlanTypeName
    };

    public void RefreshLocalizedValues() => this.OnPropertyChanged((string?)null);
}
