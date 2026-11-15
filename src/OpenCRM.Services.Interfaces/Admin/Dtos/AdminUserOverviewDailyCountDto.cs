namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO дневного счётчика обзора пользователя. </summary>
public sealed class AdminUserOverviewDailyCountDto
{
    /// <summary> Дата. </summary>
    public DateTime Date { get; init; }

    /// <summary> Подпись точки графика. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Значение счётчика. </summary>
    public int Value { get; init; }
}
