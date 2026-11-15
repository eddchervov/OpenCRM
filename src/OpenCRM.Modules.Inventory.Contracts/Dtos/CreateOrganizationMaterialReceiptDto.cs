namespace OpenCRM.Modules.Inventory.Contracts.Dtos;

/// <summary>
/// Команда прихода материала на склад организации.
/// </summary>
public sealed class CreateOrganizationMaterialReceiptDto
{
    /// <summary>Материал из scenario-catalog, если приход создается по справочнику.</summary>
    public Guid? CatalogItemId { get; init; }

    /// <summary>Название материала, если приход создается без справочника.</summary>
    public string? Name { get; init; }

    /// <summary>Единица измерения прихода.</summary>
    public string? Unit { get; init; }

    /// <summary>Количество прихода.</summary>
    public decimal Quantity { get; init; }

    /// <summary>Количество базовых единиц в одной упаковке.</summary>
    public decimal? PackageQuantity { get; init; }

    /// <summary>Себестоимость единицы прихода.</summary>
    public decimal? UnitCost { get; init; }

    /// <summary>Комментарий к приходу.</summary>
    public string? Comment { get; init; }
}
