namespace OpenCRM.Domain;

/// <summary>
/// Коды сценарных типов документов. Название шаблона можно менять, код определяет генератор.
/// </summary>
public static class OrganizationScenarioDocumentKindCodes
{
    /// <summary> Код типа документа «заказ-наряд». </summary>
    public const string WorkOrder = "work-order";

    /// <summary> Код типа документа «акт выполненных работ». </summary>
    public const string WorkAct = "work-act";

    /// <summary> Код типа документа «акт осмотра». </summary>
    public const string InspectionAct = "inspection-act";

    /// <summary> Код типа документа «фотоотчёт». </summary>
    public const string PhotoReport = "photo-report";

    /// <summary> Код типа документа «рекомендации». </summary>
    public const string Recommendations = "recommendations";

    /// <summary> Код типа документа «спецификация материалов». </summary>
    public const string MaterialsSpec = "materials-spec";

    /// <summary> Код типа документа «квитанция». </summary>
    public const string Receipt = "receipt";

    /// <summary> Код типа документа «отчёт о готовности». </summary>
    public const string ReadinessReport = "readiness-report";

    /// <summary> Код типа документа «отчёт об обслуживании». </summary>
    public const string MaintenanceReport = "maintenance-report";

    /// <summary> Код типа документа «чек-лист». </summary>
    public const string Checklist = "checklist";

    /// <summary> Нормализует код типа документа. </summary>
    public static string? Normalize(string? code)
    {
        var normalized = code?.Trim().ToLowerInvariant();
        return normalized switch
        {
            WorkOrder or "order" or "project-order" => WorkOrder,
            WorkAct or "act" or "service-act" => WorkAct,
            InspectionAct or "acceptance-act" or "intake-act" => InspectionAct,
            PhotoReport or "project-report" => PhotoReport,
            Recommendations or "client-recommendations" => Recommendations,
            MaterialsSpec or "materials" or "specification" => MaterialsSpec,
            Receipt or "check" or "cash-receipt" => Receipt,
            ReadinessReport or "ready-report" => ReadinessReport,
            Checklist or "check-list" => Checklist,
            MaintenanceReport or "report" or "client-report" => MaintenanceReport,
            _ => null
        };
    }
}
