namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Метрика сценария рабочего пространства организации. </summary>
public sealed class OrganizationWorkspaceScenarioMetricDto
{
    /// <summary> Код метрики. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Заголовок метрики. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Шаблон адреса метрики. </summary>
    public string UrlTemplate { get; init; } = string.Empty;

    /// <summary> Код требуемой функции, если есть. </summary>
    public string? RequiredFeatureCode { get; init; }
}
