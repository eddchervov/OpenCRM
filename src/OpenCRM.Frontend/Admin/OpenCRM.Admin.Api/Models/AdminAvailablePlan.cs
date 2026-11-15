namespace OpenCRM.Admin.Api.Models;

/// <summary> Тарифный план, доступный для назначения из административной панели. </summary>
public sealed class AdminAvailablePlan
{
    /// <summary> Идентификатор тарифа. </summary>
    public int Id { get; init; }

    /// <summary> Название тарифа. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Код типа тарифа. </summary>
    public string PlanTypeCode { get; init; } = string.Empty;

    /// <summary> Длительность тарифа в днях. </summary>
    public int DurationDays { get; init; }
}
