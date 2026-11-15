namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO дневной суммы обзора пользователя. </summary>
public sealed class AdminUserOverviewDailyMoneyDto
{
    /// <summary> Дата. </summary>
    public DateTime Date { get; init; }

    /// <summary> Подпись точки графика. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Значение суммы. </summary>
    public decimal Value { get; init; }
}
