using OpenCRM.Domain;

namespace OpenCRM.Modules.Inventory.Materials;

/// <summary> Правила расчёта стоимости материалов на складе. </summary>
internal static class InventoryCostRules
{
    /// <summary> Приводит денежное значение к нормализованному виду. </summary>
    internal static decimal NormalizeMoney(decimal? value) => Math.Round(Math.Max(0, value ?? 0), 4, MidpointRounding.AwayFromZero);

    /// <summary> Рассчитывает стоимость движения по количеству и цене единицы. </summary>
    internal static decimal CalculateMovementCost(decimal quantity, decimal unitCost)
        => Math.Round(Math.Abs(quantity) * unitCost, 2, MidpointRounding.AwayFromZero);

    /// <summary> Рассчитывает стоимость складских остатков. </summary>
    internal static decimal CalculateInventoryValue(decimal quantityOnHand, decimal averageUnitCost)
        => Math.Round(Math.Max(0, quantityOnHand) * Math.Max(0, averageUnitCost), 2, MidpointRounding.AwayFromZero);

    /// <summary> Обновляет среднюю стоимость остатка после приёмки. </summary>
    internal static void ApplyReceiptCost(OrganizationMaterialStock stock, decimal receiptQuantity, decimal receiptUnitCost)
    {
        if (receiptQuantity <= 0)
        {
            return;
        }

        if (receiptUnitCost <= 0)
        {
            stock.InventoryValue = CalculateInventoryValue(stock.QuantityOnHand, stock.AverageUnitCost);
            return;
        }

        var currentValue = CalculateInventoryValue(stock.QuantityOnHand, stock.AverageUnitCost);
        var receiptValue = CalculateMovementCost(receiptQuantity, receiptUnitCost);
        var nextQuantity = stock.QuantityOnHand + receiptQuantity;
        stock.AverageUnitCost = nextQuantity > 0
            ? Math.Round((currentValue + receiptValue) / nextQuantity, 8, MidpointRounding.AwayFromZero)
            : receiptUnitCost;
    }
}
