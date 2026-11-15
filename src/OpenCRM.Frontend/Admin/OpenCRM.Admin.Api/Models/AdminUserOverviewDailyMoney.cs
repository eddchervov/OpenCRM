namespace OpenCRM.Admin.Api.Models;

/// <summary> Точка графика с дневной суммой. </summary>
public sealed class AdminUserOverviewDailyMoney
{
    /// <summary> Дата точки графика. </summary>
    public DateTime Date { get; init; }

    /// <summary> Подпись точки графика. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Сумма за дату. </summary>
    public decimal Value { get; init; }
}
