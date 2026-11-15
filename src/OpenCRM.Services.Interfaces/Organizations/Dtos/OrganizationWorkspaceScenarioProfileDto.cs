using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Профиль сценария рабочего пространства организации. </summary>
public sealed record OrganizationWorkspaceScenarioProfileDto
{
    /// <summary> Код сценария. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Код источника сценария. </summary>
    public string SourceCode { get; init; } = string.Empty;

    /// <summary> Заголовок сценария. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Краткое описание сценария. </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary> Список рекомендуемых модулей. </summary>
    public IReadOnlyList<string> RecommendedModules { get; init; } = [];

    /// <summary> Список приоритетных направлений сценария. </summary>
    public IReadOnlyList<string> Focus { get; init; } = [];

    /// <summary> Список возможностей сценария. </summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary> Подсказка на панели управления. </summary>
    public string DashboardHint { get; init; } = string.Empty;

    /// <summary> Метрики сценария. </summary>
    public IReadOnlyList<OrganizationWorkspaceScenarioMetricDto> Metrics { get; init; } = [];

    /// <summary> Определение процесса сценария. </summary>
    public OrganizationScenarioProcessDefinition Process { get; init; } =
        OrganizationScenarioProcessCatalog.Resolve(null);

    /// <summary> Терминология сценария. </summary>
    public OrganizationWorkspaceScenarioTermsDto Terms { get; init; } = new();

    /// <summary> Доступные модули сценария. </summary>
    public OrganizationWorkspaceScenarioModulesDto Modules { get; init; } = new();

    /// <summary> Профиль формы обслуживания. </summary>
    public OrganizationWorkspaceMaintenanceFormProfileDto MaintenanceForm { get; init; } = new();
}
