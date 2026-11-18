namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Финансовые итоги организации в валюте исторических записей. </summary>
public sealed class AdminOrganizationCurrencyTotalDto
{
    /// <summary> Код валюты итогов. </summary>
    public string CurrencyCode { get; init; } = "RUB";

    /// <summary> Стоимость заказов после скидок. </summary>
    public decimal WorkOrdersTotal { get; init; }

    /// <summary> Оплаченная сумма по заказам. </summary>
    public decimal PaymentsTotal { get; init; }

    /// <summary> Учетная стоимость запасов. </summary>
    public decimal InventoryValue { get; init; }
}
