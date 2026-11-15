namespace OpenCRM.Domain;

/// <summary>
/// Коды статусов ручных задач планировщика.
/// </summary>
public static class OrganizationSchedulerTaskStatusCodes
{
    /// <summary>
    /// Новая задача, по которой еще не началась работа.
    /// </summary>
    public const string New = "new";

    /// <summary>
    /// Задача находится в работе.
    /// </summary>
    public const string InProgress = "in_progress";

    /// <summary>
    /// Задача отложена на будущее.
    /// </summary>
    public const string Deferred = "deferred";

    /// <summary>
    /// Задача закрыта и больше не требует действий.
    /// </summary>
    public const string Closed = "closed";

    /// <summary>
    /// Приводит внешний код статуса к одному из поддерживаемых значений.
    /// </summary>
    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        InProgress => InProgress,
        Deferred => Deferred,
        Closed => Closed,
        _ => New
    };

    /// <summary>
    /// Возвращает русское название статуса для интерфейса.
    /// </summary>
    public static string GetName(string? value) => Normalize(value) switch
    {
        InProgress => "В работе",
        Deferred => "Отложено",
        Closed => "Закрыто",
        _ => "Новая"
    };
}
