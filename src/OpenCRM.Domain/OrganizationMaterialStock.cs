namespace OpenCRM.Domain;

/// <summary>
/// Остаток материала или запчасти на складе конкретной организации.
/// </summary>
public class OrganizationMaterialStock
{
    /// <summary>Идентификатор складской позиции.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Идентификатор организации, которой принадлежит складская позиция.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>Организация-владелец складской позиции.</summary>
    public Organization Organization { get; set; } = null!;

    /// <summary>Связь со сценарным справочником материалов, если позиция создана из catalog item.</summary>
    public Guid? CatalogItemId { get; set; }

    /// <summary>Элемент scenario-catalog, описывающий материал или запчасть.</summary>
    public OrganizationScenarioCatalogItem? CatalogItem { get; set; }

    /// <summary>Название материала, которое показывается в складе и движениях.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Единица измерения остатка: шт, л, м, кг и т.д.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary> Устаревшая единица измерения остатка. </summary>
    public string? LegacyUnit { get; set; }

    /// <summary>Количество складских единиц в одной упаковке.</summary>
    public decimal? PackageQuantity { get; set; }

    /// <summary>Текущий остаток на складе.</summary>
    public decimal QuantityOnHand { get; set; }

    /// <summary>Зарезервированное количество под будущие работы, заказы или другие операции.</summary>
    public decimal ReservedQuantity { get; set; }

    /// <summary>Минимальный желаемый остаток для подсветки дефицита.</summary>
    public decimal MinQuantity { get; set; }

    /// <summary>Средняя учетная себестоимость единицы остатка.</summary>
    public decimal AverageUnitCost { get; set; }

    /// <summary>Учетная стоимость текущего остатка.</summary>
    public decimal InventoryValue { get; set; }

    /// <summary>Дата создания складской позиции.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Дата последнего изменения остатка или настроек позиции.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
