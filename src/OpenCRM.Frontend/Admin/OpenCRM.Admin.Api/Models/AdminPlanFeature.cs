namespace OpenCRM.Admin.Api.Models;

/// <summary> Возможность тарифа с её значением и признаком назначения. </summary>
public sealed class AdminPlanFeature
{
    public int FeatureId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string ScopeCode { get; init; } = string.Empty;
    public string ScopeName { get; init; } = string.Empty;
    public string AudienceCode { get; init; } = string.Empty;
    public string AudienceName { get; init; } = string.Empty;
    public string ValueType { get; init; } = string.Empty;
    public bool IsAssigned { get; set; }
    public bool? BoolValue { get; set; }
    public int? IntValue { get; set; }
}
