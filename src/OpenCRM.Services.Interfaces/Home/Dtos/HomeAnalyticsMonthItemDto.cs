namespace OpenCRM.Services.Interfaces.Home.Dtos;

/// <summary> Точка расходов за месяц на графике аналитики. </summary>
public class HomeAnalyticsMonthItemDto
{
    /// <summary> Подпись месяца. </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary> Сумма расходов за месяц. </summary>
    public decimal Total { get; set; }

    /// <summary> Доля месяца в общих расходах в процентах. </summary>
    public int Percent { get; set; }
}
