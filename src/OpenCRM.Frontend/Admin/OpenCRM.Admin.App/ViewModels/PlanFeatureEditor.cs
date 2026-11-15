using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Редактор значения функции тарифа. </summary>
public partial class PlanFeatureEditor(AdminPlanFeature feature) : ObservableObject
{
    public int FeatureId { get; } = feature.FeatureId;

    public string Name { get; } = feature.Name;

    public string Description { get; } = feature.Description;

    public string? Category { get; } = feature.Category;

    public string ValueType { get; } = feature.ValueType;

    public bool IsBooleanValue => string.Equals(this.ValueType, "Boolean", StringComparison.OrdinalIgnoreCase);

    public bool IsIntegerValue => string.Equals(this.ValueType, "Integer", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private bool _isAssigned = feature.IsAssigned;

    [ObservableProperty]
    private bool? _boolValue = feature.BoolValue;

    [ObservableProperty]
    private int? _intValue = feature.IntValue;

    /// <summary> Строит DTO для сохранения набора функций. </summary>
    public AdminPlanFeature ToRequest() => new()
    {
        FeatureId = this.FeatureId,
        IsAssigned = this.IsAssigned,
        BoolValue = this.BoolValue,
        IntValue = this.IntValue
    };
}
