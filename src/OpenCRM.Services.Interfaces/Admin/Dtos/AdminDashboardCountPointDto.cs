namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Точка графика счётчика с датой. </summary>
public sealed class AdminDashboardCountPointDto
{
    /// <summary> Дата точки. </summary>
    public DateTime Date { get; init; }

    /// <summary> Подпись точки. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Значение счётчика. </summary>
    public int Value { get; init; }
}

