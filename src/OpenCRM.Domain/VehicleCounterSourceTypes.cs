namespace OpenCRM.Domain;

/// <summary> Типы источников показаний счётчика. </summary>
public static class VehicleCounterSourceTypes
{
    /// <summary> Тип источника — ручной ввод показания. </summary>
    public const string Manual = "manual";

    /// <summary> Тип источника — показание из записи об обслуживании. </summary>
    public const string Maintenance = "maintenance";

    /// <summary> Список всех типов источников показаний. </summary>
    public static readonly IReadOnlyCollection<string> All =
    [
        Manual,
        Maintenance
    ];

    /// <summary> Приводит тип источника показания к нормализованному виду. </summary>
    public static string Normalize(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return All.Contains(normalized ?? string.Empty) ? normalized! : Manual;
    }

    /// <summary> Возвращает название типа источника показания по коду. </summary>
    public static string GetName(string? value) => Normalize(value) switch
    {
        Maintenance => "Обслуживание",
        _ => "Вручную"
    };
}
