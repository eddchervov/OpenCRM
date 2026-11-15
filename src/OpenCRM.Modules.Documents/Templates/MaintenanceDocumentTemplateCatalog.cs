namespace OpenCRM.Modules.Documents.Templates;

/// <summary> Каталог шаблонов документов обслуживания. </summary>
public static class MaintenanceDocumentTemplateCatalog
{
    /// <summary> Доступные шаблоны документов по коду. </summary>
    private static readonly IReadOnlyDictionary<string, MaintenanceDocumentTemplate> Templates =
        new[]
        {
            ServiceStationMaintenanceDocumentTemplate.Create(),
            DetailingMaintenanceDocumentTemplate.Create(),
            WrappingMaintenanceDocumentTemplate.Create(),
            CarWashMaintenanceDocumentTemplate.Create(),
            FleetMaintenanceDocumentTemplate.Create(),
            DrivingSchoolMaintenanceDocumentTemplate.Create(),
            MinimalMaintenanceDocumentTemplate.Create()
        }
        .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

    /// <summary> Выбирает шаблон по коду стиля или коду сценария. </summary>
    public static MaintenanceDocumentTemplate Resolve(string? styleCode, string? scenarioCode)
    {
        var code = string.IsNullOrWhiteSpace(styleCode) || styleCode.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? ResolveCodeByScenario(scenarioCode)
            : NormalizeCode(styleCode);

        return Templates.TryGetValue(code, out var template)
            ? template
            : Templates["service-station"];
    }

    /// <summary> Определяет код шаблона по коду сценария. </summary>
    private static string ResolveCodeByScenario(string? scenarioCode)
    {
        var normalized = NormalizeCode(scenarioCode);

        return normalized switch
        {
            "detailing" => "detailing",
            "wrapping" => "wrapping",
            "car-wash" => "car-wash",
            "fleet" => "fleet",
            "driving-school" => "driving-school",
            "personal" => "minimal",
            _ => "service-station"
        };
    }

    /// <summary> Приводит код шаблона к каноническому виду. </summary>
    private static string NormalizeCode(string? code)
    {
        var normalized = code?.Trim().ToLowerInvariant() ?? string.Empty;

        return normalized switch
        {
            "service" or "sto" or "auto-service" or "autoservice" => "service-station",
            "detail" => "detailing",
            "wrap" => "wrapping",
            "wash" or "carwash" or "car-wash" => "car-wash",
            "school" or "driving" => "driving-school",
            _ => normalized
        };
    }
}
