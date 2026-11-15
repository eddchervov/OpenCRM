namespace OpenCRM.Domain;

/// <summary>
/// Коды приоритетов ручных задач планировщика.
/// </summary>
public static class OrganizationSchedulerTaskPriorityCodes
{
    /// <summary>
    /// Низкий приоритет без срочных действий.
    /// </summary>
    public const string Low = "low";

    /// <summary>
    /// Обычный рабочий приоритет.
    /// </summary>
    public const string Normal = "normal";

    /// <summary>
    /// Высокий приоритет для важных задач.
    /// </summary>
    public const string High = "high";

    /// <summary>
    /// Срочная задача, которую нужно обработать в первую очередь.
    /// </summary>
    public const string Urgent = "urgent";

    /// <summary>
    /// Приводит внешний код приоритета к одному из поддерживаемых значений.
    /// </summary>
    public static string Normalize(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        Low => Low,
        High => High,
        Urgent => Urgent,
        _ => Normal
    };

    /// <summary>
    /// Возвращает русское название приоритета для интерфейса.
    /// </summary>
    public static string GetName(string? code) => Normalize(code) switch
    {
        Low => "Низкий",
        High => "Высокий",
        Urgent => "Срочно",
        _ => "Обычный"
    };
}
