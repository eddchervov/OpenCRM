namespace OpenCRM.Domain;

/// <summary>
/// Коды типов scenario-catalog справочников организации.
/// </summary>
public static class OrganizationScenarioCatalogKindCodes
{
    /// <summary>
    /// Типы работ и услуг, доступные для сценария организации.
    /// </summary>
    public const string WorkType = "work_type";

    /// <summary>
    /// Услуги, которые можно выбрать в записях и обслуживании.
    /// </summary>
    public const string Service = "service";

    /// <summary>Набор услуг, добавляемых в запись одним действием.</summary>
    public const string ServiceBundle = "service_bundle";

    /// <summary>
    /// Рабочие посты, боксы, места мойки или зоны выполнения работ.
    /// </summary>
    public const string WorkPost = "work_post";

    /// <summary>
    /// Документы и шаблоны документов сценария.
    /// </summary>
    public const string Document = "document";

    /// <summary>
    /// Правила планирования и ограничения рабочего процесса.
    /// </summary>
    public const string PlanningRule = "planning_rule";

    /// <summary>
    /// Материалы, запчасти и расходники.
    /// </summary>
    public const string Material = "material";

    /// <summary>
    /// Приводит внешний код типа справочника к одному из поддерживаемых значений.
    /// </summary>
    public static string Normalize(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        Document => Document,
        WorkPost => WorkPost,
        Service => Service,
        ServiceBundle => ServiceBundle,
        PlanningRule => PlanningRule,
        Material => Material,
        _ => WorkType
    };
}
