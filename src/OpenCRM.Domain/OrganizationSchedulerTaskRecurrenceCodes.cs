namespace OpenCRM.Domain;

/// <summary>
/// Коды повторяемости ручных задач планировщика.
/// </summary>
public static class OrganizationSchedulerTaskRecurrenceCodes
{
    /// <summary>
    /// Задача не повторяется.
    /// </summary>
    public const string None = "none";

    /// <summary>
    /// Ежедневное повторение задачи.
    /// </summary>
    public const string Daily = "daily";

    /// <summary>
    /// Еженедельное повторение задачи.
    /// </summary>
    public const string Weekly = "weekly";

    /// <summary>
    /// Ежемесячное повторение задачи.
    /// </summary>
    public const string Monthly = "monthly";

    /// <summary>
    /// Приводит внешний код повторяемости к одному из поддерживаемых значений.
    /// </summary>
    public static string Normalize(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        Daily => Daily,
        Weekly => Weekly,
        Monthly => Monthly,
        _ => None
    };

    /// <summary>
    /// Возвращает русское название повторяемости для интерфейса.
    /// </summary>
    public static string GetName(string? code) => Normalize(code) switch
    {
        Daily => "Каждый день",
        Weekly => "Каждую неделю",
        Monthly => "Каждый месяц",
        _ => "Не повторять"
    };
}
