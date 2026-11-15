using OpenCRM.Domain;
using OpenCRM.Modules.Inventory.Materials;

namespace OpenCRM.Modules.Inventory.Tests.Unit;

public sealed class InventoryCostRulesTests
{
    [Fact]
    public void ApplyReceiptCostCalculatesWeightedAverageAndRoundsInventoryValue()
    {
        var stock = new OrganizationMaterialStock
        {
            QuantityOnHand = 2,
            AverageUnitCost = 10,
            InventoryValue = 20
        };

        InventoryCostRules.ApplyReceiptCost(stock, 3, 20);

        Assert.Equal(16, stock.AverageUnitCost);
        Assert.Equal(80, InventoryCostRules.CalculateInventoryValue(5, stock.AverageUnitCost));
        Assert.Equal(10.01m, InventoryCostRules.CalculateMovementCost(2.5m, 4.004m));
    }

    [Fact]
    public void ApplyNonCostedReceiptKeepsAverageCost()
    {
        var stock = new OrganizationMaterialStock { QuantityOnHand = 4, AverageUnitCost = 2.5m };

        InventoryCostRules.ApplyReceiptCost(stock, 2, 0);

        Assert.Equal(2.5m, stock.AverageUnitCost);
        Assert.Equal(10, stock.InventoryValue);
    }

    [Fact]
    public void MoneyRulesClampNegativeValuesAndRoundAwayFromZero()
    {
        Assert.Equal(0, InventoryCostRules.NormalizeMoney(null));
        Assert.Equal(0, InventoryCostRules.NormalizeMoney(-10));
        Assert.Equal(1.0001m, InventoryCostRules.NormalizeMoney(1.00005m));
        Assert.Equal(0.01m, InventoryCostRules.CalculateMovementCost(-1, 0.005m));
        Assert.Equal(0, InventoryCostRules.CalculateInventoryValue(-1, 100));
        Assert.Equal(0, InventoryCostRules.CalculateInventoryValue(1, -100));
        Assert.Equal(0.01m, InventoryCostRules.CalculateInventoryValue(1, 0.005m));
    }

    [Fact]
    public void ReceiptWithoutQuantityDoesNotChangeStockCosts()
    {
        var stock = new OrganizationMaterialStock
        {
            QuantityOnHand = 4,
            AverageUnitCost = 2.5m,
            InventoryValue = 11
        };

        InventoryCostRules.ApplyReceiptCost(stock, 0, 10);

        Assert.Equal(2.5m, stock.AverageUnitCost);
        Assert.Equal(11, stock.InventoryValue);
    }

    [Fact]
    public void ReceiptThatBringsNegativeQuantityToZeroUsesReceiptUnitCost()
    {
        var stock = new OrganizationMaterialStock { QuantityOnHand = -2, AverageUnitCost = 10 };

        InventoryCostRules.ApplyReceiptCost(stock, 2, 7);

        Assert.Equal(7, stock.AverageUnitCost);
    }
}
