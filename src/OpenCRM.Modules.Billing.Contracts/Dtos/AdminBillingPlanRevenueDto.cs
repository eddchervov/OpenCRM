namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Выручка по тарифному плану. </summary>
public sealed class AdminBillingPlanRevenueDto
{
    /// <summary> Идентификатор тарифного плана. </summary>
    public int PlanId { get; init; }

    /// <summary> Название тарифного плана. </summary>
    public string Plan { get; init; } = string.Empty;

    /// <summary> Количество платежей по тарифному плану. </summary>
    public int Count { get; init; }

    /// <summary> Сумма выручки по тарифному плану. </summary>
    public decimal Amount { get; init; }
}

