namespace OpenCRM.Modules.Catalog.Contracts.Dtos;

/// <summary> Справочник организации по сценарию. </summary>
public sealed class OrganizationScenarioCatalogDto
{
    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; init; }

    /// <summary> Код сценария. </summary>
    public string ScenarioCode { get; init; } = string.Empty;

    /// <summary> Список видов работ справочника. </summary>
    public IReadOnlyList<OrganizationScenarioCatalogItemDto> WorkTypes { get; init; } = [];

    /// <summary> Список услуг справочника. </summary>
    public IReadOnlyList<OrganizationScenarioCatalogItemDto> Services { get; init; } = [];

    /// <summary> Список комплектов услуг справочника. </summary>
    public IReadOnlyList<OrganizationScenarioCatalogItemDto> ServiceBundles { get; init; } = [];

    /// <summary> Список рабочих постов справочника. </summary>
    public IReadOnlyList<OrganizationScenarioCatalogItemDto> WorkPosts { get; init; } = [];

    /// <summary> Список документов справочника. </summary>
    public IReadOnlyList<OrganizationScenarioCatalogItemDto> Documents { get; init; } = [];

    /// <summary> Список правил планирования справочника. </summary>
    public IReadOnlyList<OrganizationScenarioCatalogItemDto> PlanningRules { get; init; } = [];

    /// <summary> Список материалов справочника. </summary>
    public IReadOnlyList<OrganizationScenarioCatalogItemDto> Materials { get; init; } = [];
}
