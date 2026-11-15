namespace OpenCRM.Admin.Api.Models;

/// <summary> Точка графика со счётчиком. </summary>
public sealed class AdminDashboardCountPoint
{
    /// <summary> Дата точки. </summary>
    public DateTimeOffset Date { get; init; }

    /// <summary> Подпись для отображения. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Значение счётчика. </summary>
    public int Value { get; init; }
}
