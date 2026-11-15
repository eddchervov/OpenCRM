using OpenCRM.Modules.Inventory.Materials;

namespace OpenCRM.Modules.Inventory.Tests.Unit;

public sealed class InventoryUnitRulesTests
{
    [Theory]
    [InlineData(" кг ", "кг")]
    [InlineData("Л", "л")]
    [InlineData(null, "шт")]
    [InlineData(" ", "шт")]
    public void NormalizeUnitReturnsCanonicalSupportedUnit(string? value, string expected)
    {
        Assert.Equal(expected, InventoryUnitRules.NormalizeUnit(value));
    }

    [Theory]
    [InlineData("1500", "мл", "л", null, "1.5")]
    [InlineData("1.25", "кг", "г", null, "1250")]
    [InlineData("2", "уп", "г", "600", "1200")]
    [InlineData("2", "", "г", null, "2")]
    public void ConvertQuantityConvertsCompatibleUnitsAndPackages(
        string quantity,
        string unit,
        string stockUnit,
        string? packageQuantity,
        string expected)
    {
        decimal? parsedPackageQuantity = packageQuantity == null
            ? null
            : decimal.Parse(packageQuantity, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(
            decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            InventoryUnitRules.ConvertQuantity(
                decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture),
                unit,
                stockUnit,
                parsedPackageQuantity));
    }

    [Theory]
    [InlineData("кг", "г")]
    [InlineData("л", "мл")]
    [InlineData("шт", "шт")]
    public void ToBaseUnitConvertsOnlyMassAndVolumeUnits(string unit, string expected)
    {
        Assert.Equal(expected, InventoryUnitRules.ToBaseUnit(unit));
    }

    [Fact]
    public void ConvertQuantityRejectsUnitMismatchAndMissingPackageSize()
    {
        Assert.Throws<InvalidOperationException>(() => InventoryUnitRules.ConvertQuantity(1, "м", "л"));
        Assert.Throws<InvalidOperationException>(() => InventoryUnitRules.ConvertQuantity(1, "уп", "л"));
    }

    [Fact]
    public void NormalizeUnitRejectsUnsupportedValueAndStoredUnitsFallBackToPieces()
    {
        Assert.Throws<InvalidOperationException>(() => InventoryUnitRules.NormalizeUnit("ведро"));
        Assert.Equal("шт", InventoryUnitRules.NormalizeStoredUnit("ведро"));
        Assert.Equal("шт", InventoryUnitRules.NormalizeStoredUnit(null));
    }

    [Fact]
    public void ConvertQuantityRejectsPrecisionLossForPackageAndUnitConversion()
    {
        Assert.Throws<InvalidOperationException>(() => InventoryUnitRules.ConvertQuantity(1, "уп", "г", 0.0001m));
        Assert.Throws<InvalidOperationException>(() => InventoryUnitRules.ConvertQuantity(0.0001m, "мл", "л"));
    }

    [Theory]
    [InlineData("шт", "1.5")]
    [InlineData("компл", "0.5")]
    public void EnsureDiscreteQuantityRejectsFractions(string unit, string quantity)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => InventoryUnitRules.EnsureDiscreteQuantity(
                unit,
                decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture),
                "Количество"));

        Assert.Contains("должно быть целым", error.Message);
    }

    [Fact]
    public void EnsureDiscreteQuantityAllowsFractionalMeasureUnits()
    {
        InventoryUnitRules.EnsureDiscreteQuantity("л", 0.125m, "Количество");
    }
}
