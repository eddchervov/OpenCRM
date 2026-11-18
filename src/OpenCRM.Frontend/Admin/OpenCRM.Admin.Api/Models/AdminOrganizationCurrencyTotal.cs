namespace OpenCRM.Admin.Api.Models;

/// <summary> Финансовые итоги организации в валюте исторических записей. </summary>
public sealed class AdminOrganizationCurrencyTotal
{
    public string CurrencyCode { get; init; } = "RUB";
    public decimal WorkOrdersTotal { get; init; }
    public decimal PaymentsTotal { get; init; }
    public decimal InventoryValue { get; init; }
}
