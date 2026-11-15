using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.WebApp.Navigation;

/// <summary> Вспомогательные методы построения адресов рабочей области организации. </summary>
public static class OrganizationWorkspaceUrlHelper
{
    /// <summary> Строит адрес раздела рабочей области организации. </summary>
    public static string Resolve(OrganizationDto organization, string section = "workspace")
    {
        return Resolve(organization.Id, organization.PrimaryScenarioCode, section);
    }

    /// <summary> Строит адрес раздела рабочей области по идентификатору и коду сценария. </summary>
    public static string Resolve(Guid organizationId, string? scenarioCode, string section = "workspace")
    {
        var baseUrl = ResolveBaseUrl(organizationId, scenarioCode);
        return string.Equals(section, "workspace", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}#{section}";
    }

    /// <summary> Возвращает базовый адрес рабочей области организации. </summary>
    public static string ResolveBaseUrl(OrganizationDto organization)
    {
        return ResolveBaseUrl(organization.Id, organization.PrimaryScenarioCode);
    }

    /// <summary> Возвращает базовый адрес рабочей области по идентификатору и коду сценария. </summary>
    public static string ResolveBaseUrl(Guid organizationId, string? scenarioCode)
    {
        scenarioCode = OrganizationScenarioProcessCatalog.NormalizeScenarioCode(scenarioCode);
        return scenarioCode switch
        {
            OrganizationTypeCodes.ServiceStation => $"/autoservice/{organizationId:D}",
            OrganizationTypeCodes.Detailing => $"/detailing/{organizationId:D}",
            // HIDDEN_FLEET: маршрут сохраняет доступ участников прежних организаций. См. docs/hidden-fleet-scenario.md.
            OrganizationTypeCodes.Fleet => $"/fleet/{organizationId:D}",
            _ => "/organizations"
        };
    }

    /// <summary> Определяет, есть ли у сценария отдельная рабочая область. </summary>
    public static bool HasDedicatedWorkspace(string? scenarioCode)
    {
        scenarioCode = OrganizationScenarioProcessCatalog.NormalizeScenarioCode(scenarioCode);
        return scenarioCode is OrganizationTypeCodes.ServiceStation
            or OrganizationTypeCodes.Detailing
            or OrganizationTypeCodes.Fleet;
    }
}
