namespace OpenCRM.Services.Interfaces.Home.Dtos;

/// <summary> Аналитика для главной страницы пользователя. </summary>
public class HomeAnalyticsDto
{
    /// <summary> Количество техники пользователя. </summary>
    public int VehicleCount { get; set; }

    /// <summary> Количество записей об обслуживании. </summary>
    public int MaintenanceCount { get; set; }

    /// <summary> Общая сумма расходов на обслуживание. </summary>
    public decimal TotalSpent { get; set; }

    /// <summary> Расходы, сохранённые в других валютах. </summary>
    public IReadOnlyList<HomeAnalyticsCurrencyTotalDto> OtherCurrencyTotals { get; set; } = [];

    /// <summary> Валюта денежных показателей. </summary>
    public string CurrencyCode { get; set; } = "RUB";

    /// <summary> Средняя стоимость обслуживания. </summary>
    public decimal AverageMaintenanceCost { get; set; }

    /// <summary> Дата последнего обслуживания. </summary>
    public DateTime? LastMaintenanceDate { get; set; }

    /// <summary> Наименование техники с наибольшими расходами. </summary>
    public string? TopVehicleName { get; set; }

    /// <summary> Сумма расходов по топовой технике. </summary>
    public decimal TopVehicleSpent { get; set; }

    /// <summary> Статистика расходов по видам обслуживания. </summary>
    public List<HomeAnalyticsTypeItemDto> TypeStats { get; set; } = [];

    /// <summary> Расходы по месяцам. </summary>
    public List<HomeAnalyticsMonthItemDto> MonthlyCosts { get; set; } = [];
}
