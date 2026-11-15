namespace OpenCRM.Modules.Catalog.Contracts.Dtos;

/// <summary> Данные для изменения элемента справочника сценария организации. </summary>
public sealed class UpdateOrganizationScenarioCatalogItemDto
{
    /// <summary> Название элемента справочника. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Описание элемента справочника. </summary>
    public string? Description { get; init; }

    /// <summary> Цена за единицу услуги. </summary>
    public decimal? UnitPrice { get; init; }

    /// <summary> Длительность услуги в минутах. </summary>
    public int? DurationMinutes { get; init; }

    /// <summary> Идентификаторы услуг, входящих в элемент. </summary>
    public Guid[] ServiceIds { get; init; } = [];

    /// <summary> Код вида документа. </summary>
    public string? DocumentKindCode { get; init; }

    /// <summary> Порядок сортировки элемента. </summary>
    public int SortOrder { get; init; }

    /// <summary> Признак активного элемента справочника. </summary>
    public bool IsActive { get; init; }
}
