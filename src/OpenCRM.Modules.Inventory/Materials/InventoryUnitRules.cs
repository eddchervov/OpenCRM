namespace OpenCRM.Modules.Inventory.Materials;

/// <summary> Правила работы с единицами измерения материалов. </summary>
internal static class InventoryUnitRules
{
    /// <summary> Приводит единицу измерения к нормализованному виду. </summary>
    internal static string NormalizeUnit(string? value) => OpenCRM.Domain.Materials.MaterialUnitRules.Normalize(
        value,
        "Выберите единицу измерения из списка: шт, л, мл, кг, г, м, м2 или компл.");

    /// <summary> Приводит единицу измерения для хранения к нормализованному виду. </summary>
    internal static string NormalizeStoredUnit(string? value) => OpenCRM.Domain.Materials.MaterialUnitRules.NormalizeStored(value);

    /// <summary> Проверяет, что количество соответствует дискретной единице измерения. </summary>
    internal static void EnsureDiscreteQuantity(string? unit, decimal quantity, string fieldName)
        => OpenCRM.Domain.Materials.MaterialUnitRules.EnsureDiscreteQuantity(unit, quantity, fieldName);

    /// <summary> Переводит количество в единицу измерения склада. </summary>
    internal static decimal ConvertQuantity(decimal quantity, string? unit, string stockUnit, decimal? packageQuantity = null)
    {
        var normalizedStockUnit = NormalizeStoredUnit(stockUnit);
        if (unit == "уп")
        {
            if (!packageQuantity.HasValue)
            {
                throw new InvalidOperationException("Для материала не задано количество в упаковке.");
            }

            var packageAmount = quantity * packageQuantity.Value;
            if (packageAmount != decimal.Round(packageAmount, 3))
            {
                throw new InvalidOperationException("Количество после перевода упаковок должно иметь не более трех знаков после запятой.");
            }

            return packageAmount;
        }

        var requestedUnit = string.IsNullOrWhiteSpace(unit) ? normalizedStockUnit : NormalizeUnit(unit);
        var converted = requestedUnit == normalizedStockUnit ? quantity : (requestedUnit, normalizedStockUnit) switch
        {
            ("г", "кг") or ("мл", "л") => quantity / 1000m,
            ("кг", "г") or ("л", "мл") => quantity * 1000m,
            _ => throw new InvalidOperationException("Единица выдачи не соответствует единице складского учета.")
        };
        if (converted != decimal.Round(converted, 3))
        {
            throw new InvalidOperationException("Количество после перевода единиц должно иметь не более трех знаков после запятой.");
        }

        return converted;
    }

    /// <summary> Возвращает базовую единицу измерения для заданной единицы. </summary>
    internal static string ToBaseUnit(string unit) => unit switch
    {
        "кг" => "г",
        "л" => "мл",
        _ => unit
    };
}
