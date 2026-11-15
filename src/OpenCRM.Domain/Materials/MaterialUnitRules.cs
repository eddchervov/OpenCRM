namespace OpenCRM.Domain.Materials;

public static class MaterialUnitRules
{
    private static readonly string[] s_allowedUnitValues = ["шт", "л", "мл", "кг", "г", "м", "м2", "компл"];
    private static readonly HashSet<string> s_allowedUnits = new(MaterialUnitRules.s_allowedUnitValues, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> s_discreteUnits = ["шт", "компл"];

    public static string Normalize(string? value, string errorMessage)
    {
        var unit = value?.Trim();
        if (string.IsNullOrWhiteSpace(unit))
        {
            return "шт";
        }

        if (!MaterialUnitRules.s_allowedUnits.Contains(unit))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return MaterialUnitRules.s_allowedUnitValues.First(allowedUnit => string.Equals(allowedUnit, unit, StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeStored(string? value)
    {
        var unit = value?.Trim();
        if (string.IsNullOrWhiteSpace(unit) || !MaterialUnitRules.s_allowedUnits.Contains(unit))
        {
            return "шт";
        }

        return MaterialUnitRules.s_allowedUnitValues.First(allowedUnit => string.Equals(allowedUnit, unit, StringComparison.OrdinalIgnoreCase));
    }

    public static void EnsureDiscreteQuantity(string? unit, decimal quantity, string fieldName)
    {
        var normalizedUnit = MaterialUnitRules.NormalizeStored(unit);
        if (MaterialUnitRules.s_discreteUnits.Contains(normalizedUnit) && decimal.Truncate(quantity) != quantity)
        {
            throw new InvalidOperationException($"{fieldName} для единицы \"{normalizedUnit}\" должно быть целым.");
        }
    }
}
