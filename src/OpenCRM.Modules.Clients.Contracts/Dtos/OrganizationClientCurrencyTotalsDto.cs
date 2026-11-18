namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Оплаты и долг клиента в валюте исходных записей. </summary>
public sealed class OrganizationClientCurrencyTotalsDto
{
    public string CurrencyCode { get; init; } = "RUB";

    public decimal TotalPaidAmount { get; init; }

    public decimal TotalDebtAmount { get; init; }
}
