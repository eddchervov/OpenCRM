namespace OpenCRM.Admin.Api.Models;

/// <summary> Ежедневная сумма платежей. </summary>
public sealed class AdminBillingDailyMoney
{
    /// <summary> Дата. </summary>
    public DateTime Date { get; init; }

    /// <summary> Подпись для отображения на графике. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Сумма платежей за день. </summary>
    public decimal Amount { get; init; }

    /// <summary> Количество платежей за день. </summary>
    public int Count { get; init; }
}
