namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Сводка ситуаций в биллинге, требующих внимания администратора. </summary>
public sealed class AdminBillingAttentionDto
{
    /// <summary> Период в днях, за который собрана сводка. </summary>
    public int? PeriodDays { get; init; }

    /// <summary> Количество платежей без квитанции. </summary>
    public int MissingReceiptCount { get; init; }

    /// <summary> Количество просроченных квитанций. </summary>
    public int OverdueReceiptCount { get; init; }

    /// <summary> Количество неудачных активаций. </summary>
    public int FailedActivationCount { get; init; }
}
