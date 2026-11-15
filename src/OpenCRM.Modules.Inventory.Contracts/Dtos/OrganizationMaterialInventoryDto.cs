namespace OpenCRM.Modules.Inventory.Contracts.Dtos;

/// <summary>
/// Сводка склада организации: текущие остатки и последние движения.
/// </summary>
public sealed class OrganizationMaterialInventoryDto
{
    /// <summary>Текущие складские остатки организации.</summary>
    public IReadOnlyList<OrganizationMaterialStockDto> Stocks { get; init; } = [];

    /// <summary>Последние движения материалов для журнала склада.</summary>
    public IReadOnlyList<OrganizationMaterialMovementDto> RecentMovements { get; init; } = [];

    /// <summary>Общее количество складских позиций организации без поиска и фильтров.</summary>
    public int AllStocksCount { get; init; }

    /// <summary>Общее количество складских позиций до постраничной выдачи.</summary>
    public int StocksTotalCount { get; init; }

    /// <summary>Общее количество движений до постраничной выдачи.</summary>
    public int MovementsTotalCount { get; init; }

    /// <summary>Текущая страница остатков.</summary>
    public int StocksPage { get; init; } = 1;

    /// <summary>Размер страницы остатков.</summary>
    public int StocksPageSize { get; init; } = 20;

    /// <summary>Текущая страница журнала движений.</summary>
    public int MovementsPage { get; init; } = 1;

    /// <summary>Размер страницы журнала движений.</summary>
    public int MovementsPageSize { get; init; } = 20;

    /// <summary>Количество позиций ниже минимального остатка.</summary>
    public int LowStockCount { get; init; }

    /// <summary>Количество позиций с нулевым или отрицательным остатком.</summary>
    public int OutOfStockCount { get; init; }

    /// <summary>Суммарное количество всех остатков без учета единиц измерения.</summary>
    public decimal TotalQuantityOnHand { get; init; }

    /// <summary>Суммарная учетная стоимость всех остатков.</summary>
    public decimal TotalInventoryValue { get; init; }

    /// <summary>Дата и время рассчитанного среза склада, если клиент запросил отчет.</summary>
    public DateTime? SnapshotAt { get; init; }

    /// <summary>Остатки склада на выбранные дату и время.</summary>
    public IReadOnlyList<OrganizationMaterialStockSnapshotDto> SnapshotStocks { get; init; } = [];

    /// <summary>Суммарная учетная стоимость склада на выбранные дату и время.</summary>
    public decimal SnapshotTotalInventoryValue { get; init; }
}
