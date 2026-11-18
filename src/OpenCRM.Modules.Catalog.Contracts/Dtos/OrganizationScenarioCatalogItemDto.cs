namespace OpenCRM.Modules.Catalog.Contracts.Dtos;

/// <summary> Элемент справочника сценария организации. </summary>
public sealed class OrganizationScenarioCatalogItemDto
{
    /// <summary> Идентификатор элемента справочника. </summary>
    public Guid Id { get; init; }

    /// <summary> Код сценария элемента. </summary>
    public string ScenarioCode { get; init; } = string.Empty;

    /// <summary> Код вида элемента. </summary>
    public string KindCode { get; init; } = string.Empty;

    /// <summary> Код элемента справочника. </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary> Название элемента справочника. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Описание элемента справочника. </summary>
    public string? Description { get; init; }

    /// <summary> Цена за единицу услуги. </summary>
    public decimal? UnitPrice { get; init; }

    /// <summary> Валюта цены элемента каталога. </summary>
    public string CurrencyCode { get; init; } = "RUB";

    /// <summary> Длительность услуги в минутах. </summary>
    public int? DurationMinutes { get; init; }

    /// <summary> Идентификаторы услуг, входящих в элемент. </summary>
    public Guid[] ServiceIds { get; init; } = [];

    /// <summary> Код вида документа. </summary>
    public string? DocumentKindCode { get; init; }

    /// <summary> Порядок сортировки элемента. </summary>
    public int SortOrder { get; init; }

    /// <summary> Признак системного элемента справочника. </summary>
    public bool IsSystem { get; init; }

    /// <summary> Признак использования элемента. </summary>
    public bool IsUsed { get; init; }

    /// <summary> Признак активного элемента. </summary>
    public bool IsActive { get; init; }
}
