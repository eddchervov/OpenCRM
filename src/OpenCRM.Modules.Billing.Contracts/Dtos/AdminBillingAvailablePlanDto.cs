namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Тарифный план, доступный для выбора в админке биллинга. </summary>
public sealed class AdminBillingAvailablePlanDto
{
    /// <summary> Идентификатор тарифного плана. </summary>
    public int Id { get; init; }

    /// <summary> Название тарифного плана. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Код типа тарифного плана. </summary>
    public string PlanTypeCode { get; init; } = string.Empty;

    /// <summary> Длительность тарифного плана в днях. </summary>
    public int DurationDays { get; init; }
}
