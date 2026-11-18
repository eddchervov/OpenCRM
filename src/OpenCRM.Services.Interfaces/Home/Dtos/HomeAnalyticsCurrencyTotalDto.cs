namespace OpenCRM.Services.Interfaces.Home.Dtos;

/// <summary> Сумма расходов и количество обслуживаний в одной валюте. </summary>
public sealed class HomeAnalyticsCurrencyTotalDto
{
    public string CurrencyCode { get; init; } = "RUB";

    public decimal TotalSpent { get; init; }

    public int MaintenanceCount { get; init; }
}
