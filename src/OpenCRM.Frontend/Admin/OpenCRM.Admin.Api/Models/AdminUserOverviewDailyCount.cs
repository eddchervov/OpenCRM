namespace OpenCRM.Admin.Api.Models;

/// <summary> Точка графика с дневным количеством. </summary>
public sealed class AdminUserOverviewDailyCount
{
    /// <summary> Дата точки графика. </summary>
    public DateTime Date { get; init; }

    /// <summary> Подпись точки графика. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Количество за дату. </summary>
    public int Value { get; init; }
}
