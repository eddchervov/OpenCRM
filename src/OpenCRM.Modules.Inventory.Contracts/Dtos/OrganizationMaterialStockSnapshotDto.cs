namespace OpenCRM.Modules.Inventory.Contracts.Dtos;

/// <summary>
/// Строка отчета по складским остаткам на конкретные дату и время.
/// </summary>
public sealed class OrganizationMaterialStockSnapshotDto
{
    /// <summary>Идентификатор складской позиции.</summary>
    public Guid StockId { get; init; }

    /// <summary>Связанный элемент scenario-catalog материалов, если он есть.</summary>
    public Guid? CatalogItemId { get; init; }

    /// <summary>Название материала или запчасти.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Единица измерения остатка.</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>Остаток на выбранный момент.</summary>
    public decimal QuantityOnHand { get; init; }

    /// <summary>Средняя учетная себестоимость единицы на выбранный момент.</summary>
    public decimal AverageUnitCost { get; init; }

    /// <summary>Учетная стоимость остатка на выбранный момент.</summary>
    public decimal InventoryValue { get; init; }
}
