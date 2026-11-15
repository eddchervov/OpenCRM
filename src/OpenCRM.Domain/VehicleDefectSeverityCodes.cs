namespace OpenCRM.Domain;

/// <summary> Коды серьёзности неисправностей техники. </summary>
public static class VehicleDefectSeverityCodes
{
    /// <summary> Низкая серьёзность неисправности. </summary>
    public const string Low = "low";

    /// <summary> Средняя серьёзность неисправности. </summary>
    public const string Medium = "medium";

    /// <summary> Высокая серьёзность неисправности. </summary>
    public const string High = "high";

    /// <summary> Критическая серьёзность неисправности. </summary>
    public const string Critical = "critical";

    /// <summary> Список всех кодов серьёзности неисправностей. </summary>
    public static readonly IReadOnlyCollection<string> All =
    [
        Low,
        Medium,
        High,
        Critical
    ];

    /// <summary> Приводит код серьёзности неисправности к нормализованному виду. </summary>
    public static string Normalize(string? code)
    {
        var normalized = code?.Trim().ToLowerInvariant();
        return All.Contains(normalized ?? string.Empty) ? normalized! : Medium;
    }

    /// <summary> Возвращает название серьёзности неисправности по коду. </summary>
    public static string GetName(string? code) => Normalize(code) switch
    {
        Low => "Низкая",
        High => "Высокая",
        Critical => "Критичная",
        _ => "Средняя"
    };
}
