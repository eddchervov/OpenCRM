namespace OpenCRM.Modules.Inventory.Contracts.Dtos;

/// <summary>
/// Остаток материала или запчасти на складе организации для MVC/API-клиентов.
/// </summary>
public sealed class OrganizationMaterialStockDto
{
    /// <summary>Идентификатор складской позиции.</summary>
    public Guid Id { get; init; }

    /// <summary>Связанный элемент scenario-catalog материалов, если он есть.</summary>
    public Guid? CatalogItemId { get; init; }

    /// <summary>Название материала или запчасти.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Единица измерения остатка.</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>Количество складских единиц в одной упаковке.</summary>
    public decimal? PackageQuantity { get; init; }

    /// <summary>Текущий остаток на складе.</summary>
    public decimal QuantityOnHand { get; init; }

    /// <summary>Зарезервированное количество под будущие работы или заказы.</summary>
    public decimal ReservedQuantity { get; init; }

    /// <summary>Доступный остаток за вычетом резерва.</summary>
    public decimal AvailableQuantity { get; init; }

    /// <summary>Минимальный желаемый остаток для контроля дефицита.</summary>
    public decimal MinQuantity { get; init; }

    /// <summary>Средняя учетная себестоимость единицы.</summary>
    public decimal AverageUnitCost { get; init; }

    /// <summary>Учетная стоимость текущего остатка.</summary>
    public decimal InventoryValue { get; init; }

    /// <summary>Дата последнего изменения складской позиции.</summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>Признак дефицита: остаток меньше или равен настроенному минимуму.</summary>
    public bool IsLowStock { get; init; }

    /// <summary>Признак нулевого или отрицательного остатка.</summary>
    public bool IsOutOfStock { get; init; }
}
