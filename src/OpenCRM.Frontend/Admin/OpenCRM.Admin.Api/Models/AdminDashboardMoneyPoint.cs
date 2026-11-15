namespace OpenCRM.Admin.Api.Models;

/// <summary> Точка графика с денежной суммой. </summary>
public sealed class AdminDashboardMoneyPoint
{
    /// <summary> Дата точки. </summary>
    public DateTimeOffset Date { get; init; }

    /// <summary> Подпись для отображения. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Денежная сумма. </summary>
    public decimal Value { get; init; }
}
