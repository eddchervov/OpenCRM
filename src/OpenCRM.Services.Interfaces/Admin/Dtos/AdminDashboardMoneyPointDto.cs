namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Точка графика платежей с датой. </summary>
public sealed class AdminDashboardMoneyPointDto
{
    /// <summary> Дата точки. </summary>
    public DateTime Date { get; init; }

    /// <summary> Подпись точки. </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary> Сумма платежа за день. </summary>
    public decimal Value { get; init; }
}

