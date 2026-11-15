namespace OpenCRM.Domain;

/// <summary> Коды статусов неисправностей техники. </summary>
public static class VehicleDefectStatusCodes
{
    /// <summary> Код статуса неисправности — новая. </summary>
    public const string New = "new";

    /// <summary> Код статуса неисправности — в работе. </summary>
    public const string InProgress = "in_progress";

    /// <summary> Код статуса неисправности — устранена. </summary>
    public const string Resolved = "resolved";

    /// <summary> Код статуса неисправности — отложена. </summary>
    public const string Deferred = "deferred";

    /// <summary> Список всех кодов статусов неисправностей. </summary>
    public static readonly IReadOnlyCollection<string> All =
    [
        New,
        InProgress,
        Resolved,
        Deferred
    ];

    /// <summary> Приводит код статуса неисправности к нормализованному виду. </summary>
    public static string Normalize(string? code)
    {
        var normalized = code?.Trim().ToLowerInvariant();
        return All.Contains(normalized ?? string.Empty) ? normalized! : New;
    }

    /// <summary> Возвращает название статуса неисправности по коду. </summary>
    public static string GetName(string? code) => Normalize(code) switch
    {
        InProgress => "В работе",
        Resolved => "Устранена",
        Deferred => "Отложена",
        _ => "Новая"
    };

    /// <summary> Определяет, относится ли код статуса к закрытым состояниям неисправности. </summary>
    public static bool IsClosed(string? code) => Normalize(code) == Resolved;
}
