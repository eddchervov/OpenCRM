namespace OpenCRM.Domain;

/// <summary>
/// Коды типов ручных задач планировщика организации.
/// </summary>
public static class OrganizationSchedulerTaskTypeCodes
{
    /// <summary>
    /// Произвольная задача без специального сценария.
    /// </summary>
    public const string Other = "other";

    /// <summary>
    /// Задача на звонок клиенту или сотруднику.
    /// </summary>
    public const string Call = "call";

    /// <summary>
    /// Осмотр техники или предварительная диагностика.
    /// </summary>
    public const string Inspection = "inspection";

    /// <summary>
    /// Работы по обслуживанию или ремонту.
    /// </summary>
    public const string Maintenance = "maintenance";

    /// <summary>
    /// Подготовка, проверка или выдача документа.
    /// </summary>
    public const string Document = "document";

    /// <summary>
    /// Передача, доставка или приемка техники.
    /// </summary>
    public const string Delivery = "delivery";

    /// <summary>
    /// Приводит внешний код типа задачи к одному из поддерживаемых значений.
    /// </summary>
    public static string Normalize(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        Call => Call,
        Inspection => Inspection,
        Maintenance => Maintenance,
        Document => Document,
        Delivery => Delivery,
        _ => Other
    };

    /// <summary>
    /// Возвращает русское название типа задачи для интерфейса.
    /// </summary>
    public static string GetName(string? code) => Normalize(code) switch
    {
        Call => "Звонок",
        Inspection => "Осмотр",
        Maintenance => "Работа",
        Document => "Документ",
        Delivery => "Передача",
        _ => "Другое"
    };
}
