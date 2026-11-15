namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Сумма платежей за один день. </summary>
public sealed class AdminBillingDailyMoneyDto
{
    /// <summary> Дата дня. </summary>
    public DateTime Date { get; init; }

    /// <summary> Подпись дня на диаграмме. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Сумма платежей за день. </summary>
    public decimal Amount { get; init; }

    /// <summary> Количество платежей за день. </summary>
    public int Count { get; init; }
}

