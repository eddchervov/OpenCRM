using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Modules.Inventory.Materials;

namespace OpenCRM.Modules.Inventory;

/// <summary> Читает остатки и движения материалов организации. </summary>
public sealed class MaterialInventoryQueryService : IInventoryQueryService
{
    /// <summary> Контекст базы данных для чтения остатков. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Проверка подписки организации. </summary>
    private readonly IOrganizationSubscriptionService _organizationSubscriptionService;

    /// <summary> Создаёт сервис с контекстом и проверкой подписки. </summary>
    public MaterialInventoryQueryService(
        OpenCRMContext context,
        IOrganizationSubscriptionService organizationSubscriptionService)
    {
        this._context = context;
        this._organizationSubscriptionService = organizationSubscriptionService;
    }

    /// <summary> Возвращает остатки и движения материалов организации. </summary>
    public async Task<OrganizationMaterialInventoryDto> GetAsync(string userId, Guid organizationId)
        => await this.GetAsync(userId, organizationId, null, null, null, 1, 1000, 1, 30, null, null, null);

    /// <summary>
    /// Возвращает склад с фильтрами по остаткам и журналу движений.
    /// </summary>
    public async Task<OrganizationMaterialInventoryDto> GetAsync(
        string userId,
        Guid organizationId,
        string? searchQuery,
        string? stockFilter,
        string? movementType,
        int stocksPage,
        int stocksPageSize,
        int movementsPage,
        int movementsPageSize,
        DateTime? movementDateFrom = null,
        DateTime? movementDateTo = null,
        DateTime? snapshotAt = null,
        Guid? stockId = null)
    {
        await this._organizationSubscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.MaterialsView);

        var normalizedSearch = searchQuery?.Trim();
        var normalizedStockFilter = stockFilter?.Trim().ToLowerInvariant();
        var normalizedMovementType = OrganizationMaterialMovementTypeCodes.NormalizeNullable(movementType);
        var normalizedMovementDateFrom = MaterialInventoryQueryService.NormalizeDateTimeUtc(movementDateFrom);
        var normalizedMovementDateTo = MaterialInventoryQueryService.NormalizeDateTimeUtc(movementDateTo);
        var normalizedSnapshotAt = MaterialInventoryQueryService.NormalizeDateTimeUtc(snapshotAt);
        var normalizedStocksPage = Math.Max(1, stocksPage);
        var normalizedMovementsPage = Math.Max(1, movementsPage);
        var normalizedStocksPageSize = Math.Clamp(stocksPageSize, 5, 100);
        var normalizedMovementsPageSize = Math.Clamp(movementsPageSize, 5, 100);

        var allStocksQuery = this._context.OrganizationMaterialStocks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        var allStocksCount = await allStocksQuery.CountAsync();
        var lowStockCount = await allStocksQuery.CountAsync(x => x.QuantityOnHand <= x.MinQuantity && x.QuantityOnHand > 0);
        var outOfStockCount = await allStocksQuery.CountAsync(x => x.QuantityOnHand <= 0);
        var totalQuantity = await allStocksQuery.SumAsync(x => (decimal?)x.QuantityOnHand) ?? 0;
        var totalInventoryValue = await allStocksQuery.SumAsync(x => (decimal?)x.InventoryValue) ?? 0;

        var stocksQuery = allStocksQuery;
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            stocksQuery = stocksQuery.Where(x => EF.Functions.ILike(x.Name, $"%{normalizedSearch}%")
                || (x.CatalogItem != null && EF.Functions.ILike(x.CatalogItem.Name, $"%{normalizedSearch}%")));
        }

        stocksQuery = normalizedStockFilter switch
        {
            "low" => stocksQuery.Where(x => x.QuantityOnHand <= x.MinQuantity && x.QuantityOnHand > 0),
            "out" => stocksQuery.Where(x => x.QuantityOnHand <= 0),
            "ok" => stocksQuery.Where(x => x.QuantityOnHand > x.MinQuantity),
            _ => stocksQuery
        };

        var stocksTotalCount = await stocksQuery.CountAsync();
        var stockRows = await stocksQuery
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Unit)
            .Skip((normalizedStocksPage - 1) * normalizedStocksPageSize)
            .Take(normalizedStocksPageSize)
            .Select(x => new OrganizationMaterialStockDto
            {
                Id = x.Id,
                CatalogItemId = x.CatalogItemId,
                Name = x.Name,
                Unit = x.Unit,
                PackageQuantity = x.PackageQuantity,
                QuantityOnHand = x.QuantityOnHand,
                ReservedQuantity = x.ReservedQuantity,
                AvailableQuantity = x.QuantityOnHand - x.ReservedQuantity,
                MinQuantity = x.MinQuantity,
                AverageUnitCost = x.AverageUnitCost,
                InventoryValue = x.InventoryValue,
                UpdatedAt = x.UpdatedAt,
                IsLowStock = x.QuantityOnHand <= x.MinQuantity && x.QuantityOnHand > 0,
                IsOutOfStock = x.QuantityOnHand <= 0
            })
            .ToListAsync();
        var stocks = stockRows
            .Select(x => new OrganizationMaterialStockDto
            {
                Id = x.Id,
                CatalogItemId = x.CatalogItemId,
                Name = x.Name,
                Unit = InventoryUnitRules.NormalizeStoredUnit(x.Unit),
                PackageQuantity = x.PackageQuantity,
                QuantityOnHand = x.QuantityOnHand,
                ReservedQuantity = x.ReservedQuantity,
                AvailableQuantity = x.AvailableQuantity,
                MinQuantity = x.MinQuantity,
                AverageUnitCost = x.AverageUnitCost,
                InventoryValue = x.InventoryValue,
                UpdatedAt = x.UpdatedAt,
                IsLowStock = x.IsLowStock,
                IsOutOfStock = x.IsOutOfStock
            })
            .ToList();

        var movementsQuery = this._context.OrganizationMaterialMovements
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId);

        if (stockId.HasValue)
        {
            movementsQuery = movementsQuery.Where(x => x.StockId == stockId.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            movementsQuery = movementsQuery.Where(x => EF.Functions.ILike(x.Stock.Name, $"%{normalizedSearch}%")
                || (x.Comment != null && EF.Functions.ILike(x.Comment, $"%{normalizedSearch}%")));
        }

        if (!string.IsNullOrWhiteSpace(normalizedMovementType))
        {
            movementsQuery = movementsQuery.Where(x => x.TypeCode == normalizedMovementType);
        }

        if (normalizedMovementDateFrom.HasValue)
        {
            movementsQuery = movementsQuery.Where(x => x.CreatedAt >= normalizedMovementDateFrom.Value);
        }

        if (normalizedMovementDateTo.HasValue)
        {
            movementsQuery = movementsQuery.Where(x => x.CreatedAt <= normalizedMovementDateTo.Value);
        }

        var movementsTotalCount = await movementsQuery.CountAsync();
        var movementRows = await movementsQuery
            .OrderByDescending(x => x.CreatedAt)
            .Skip((normalizedMovementsPage - 1) * normalizedMovementsPageSize)
            .Take(normalizedMovementsPageSize)
            .Select(x => new OrganizationMaterialMovementDto
            {
                Id = x.Id,
                StockId = x.StockId,
                CatalogItemId = x.CatalogItemId,
                MaintenanceId = x.MaintenanceId,
                OperationId = x.OperationId,
                EmployeeId = x.EmployeeId,
                MemberId = x.MemberId,
                MaterialName = x.Stock.Name,
                TypeCode = x.TypeCode,
                Quantity = x.Quantity,
                BalanceAfter = x.QuantityBalanceAfter,
                UnitCost = x.UnitCost,
                TotalCost = x.TotalCost,
                CostBalanceAfter = x.CostBalanceAfter,
                SourceEntityType = x.SourceEntityType,
                SourceEntityId = x.SourceEntityId,
                CreatedByUserId = x.CreatedByUserId,
                Unit = x.Unit,
                Comment = x.Comment,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync();

        movementRows = (await this.EnrichMovementsAsync(movementRows)).ToList();
        movementRows = movementRows
            .Select(x => new OrganizationMaterialMovementDto
            {
                Id = x.Id,
                StockId = x.StockId,
                CatalogItemId = x.CatalogItemId,
                MaintenanceId = x.MaintenanceId,
                OperationId = x.OperationId,
                EmployeeId = x.EmployeeId,
                MemberId = x.MemberId,
                MaterialName = x.MaterialName,
                TypeCode = x.TypeCode,
                Quantity = x.Quantity,
                BalanceAfter = x.BalanceAfter,
                UnitCost = x.UnitCost,
                TotalCost = x.TotalCost,
                CostBalanceAfter = x.CostBalanceAfter,
                SourceEntityType = x.SourceEntityType,
                SourceEntityId = x.SourceEntityId,
                SourceTitle = x.SourceTitle,
                SourceUrl = x.SourceUrl,
                CreatedByUserId = x.CreatedByUserId,
                CreatedByDisplayName = x.CreatedByDisplayName,
                Unit = InventoryUnitRules.NormalizeStoredUnit(x.Unit),
                Comment = x.Comment,
                CreatedAt = x.CreatedAt
            })
            .ToList();

        var movements = await this.PopulateBalanceAfterAsync(organizationId, movementRows);
        var snapshotStocks = normalizedSnapshotAt.HasValue
            ? await this.BuildSnapshotAsync(organizationId, normalizedSnapshotAt.Value)
            : Array.Empty<OrganizationMaterialStockSnapshotDto>();

        return new OrganizationMaterialInventoryDto
        {
            Stocks = stocks,
            RecentMovements = movements,
            AllStocksCount = allStocksCount,
            StocksTotalCount = stocksTotalCount,
            MovementsTotalCount = movementsTotalCount,
            StocksPage = normalizedStocksPage,
            StocksPageSize = normalizedStocksPageSize,
            MovementsPage = normalizedMovementsPage,
            MovementsPageSize = normalizedMovementsPageSize,
            LowStockCount = lowStockCount,
            OutOfStockCount = outOfStockCount,
            TotalQuantityOnHand = totalQuantity,
            TotalInventoryValue = totalInventoryValue,
            SnapshotAt = normalizedSnapshotAt,
            SnapshotStocks = snapshotStocks,
            SnapshotTotalInventoryValue = snapshotStocks.Sum(x => x.InventoryValue)
        };
    }

    /// <summary> Дополняет движения материалов остатком после операции. </summary>
    private async Task<IReadOnlyList<OrganizationMaterialMovementDto>> PopulateBalanceAfterAsync(
        Guid organizationId,
        IReadOnlyList<OrganizationMaterialMovementDto> movements)
    {
        if (movements.Count == 0)
        {
            return movements;
        }

        var stockIds = movements.Select(x => x.StockId).Distinct().ToList();
        var oldestDate = movements.Min(x => x.CreatedAt);
        var relevantMovements = await this._context.OrganizationMaterialMovements
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId
                && stockIds.Contains(x.StockId)
                && x.CreatedAt >= oldestDate)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.StockId,
                x.Quantity,
                CurrentQuantity = x.Stock.QuantityOnHand
            })
            .ToListAsync();

        var balancesByMovementId = new Dictionary<Guid, decimal>();
        foreach (var group in relevantMovements.GroupBy(x => x.StockId))
        {
            var runningBalance = group.First().CurrentQuantity;
            foreach (var movement in group)
            {
                balancesByMovementId[movement.Id] = runningBalance;
                runningBalance -= movement.Quantity;
            }
        }

        return movements
            .Select(x => new OrganizationMaterialMovementDto
            {
                Id = x.Id,
                StockId = x.StockId,
                CatalogItemId = x.CatalogItemId,
                MaintenanceId = x.MaintenanceId,
                OperationId = x.OperationId,
                EmployeeId = x.EmployeeId,
                MemberId = x.MemberId,
                MaterialName = x.MaterialName,
                TypeCode = x.TypeCode,
                Quantity = x.Quantity,
                BalanceAfter = balancesByMovementId.TryGetValue(x.Id, out var balanceAfter) ? balanceAfter : x.BalanceAfter,
                UnitCost = x.UnitCost,
                TotalCost = x.TotalCost,
                CostBalanceAfter = x.CostBalanceAfter,
                SourceEntityType = x.SourceEntityType,
                SourceEntityId = x.SourceEntityId,
                SourceTitle = x.SourceTitle,
                SourceUrl = x.SourceUrl,
                CreatedByUserId = x.CreatedByUserId,
                CreatedByDisplayName = x.CreatedByDisplayName,
                Unit = x.Unit,
                Comment = x.Comment,
                CreatedAt = x.CreatedAt
            })
            .ToList();
    }

    /// <summary>
    /// Добавляет к движениям понятные источник и инициатора без расширения таблицы движений.
    /// </summary>
    private async Task<IReadOnlyList<OrganizationMaterialMovementDto>> EnrichMovementsAsync(IReadOnlyList<OrganizationMaterialMovementDto> movements)
    {
        if (movements.Count == 0)
        {
            return movements;
        }

        var userIds = movements
            .Select(x => x.CreatedByUserId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();
        var usersById = userIds.Count == 0
            ? new Dictionary<string, string>()
            : await this._context.ApplicationUsers
                .AsNoTracking()
                .Where(x => userIds.Contains(x.Id))
                .Select(x => new
                {
                    x.Id,
                    DisplayName = x.Email ?? x.UserName ?? x.PhoneNumber ?? x.Id
                })
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName);

        var maintenanceIds = movements
            .Select(x => x.MaintenanceId ?? (string.Equals(x.SourceEntityType, "maintenance", StringComparison.OrdinalIgnoreCase) ? x.SourceEntityId : null))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var maintenancesById = maintenanceIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await this._context.Maintenance
                .AsNoTracking()
                .Where(x => maintenanceIds.Contains(x.Id))
                .Select(x => new
                {
                    x.Id,
                    x.WorkActNumber,
                    x.PlannedDate,
                    x.Date,
                    VehicleName = x.Vehicle.Name,
                    x.Vehicle.RegistrationPlate
                })
                .ToDictionaryAsync(
                    x => x.Id,
                    x =>
                    {
                        var vehicle = string.Join(" · ", new[] { x.VehicleName, x.RegistrationPlate }.Where(v => !string.IsNullOrWhiteSpace(v)));
                        var number = x.WorkActNumber.HasValue ? $"акт #{x.WorkActNumber.Value}" : "запись";
                        var date = (x.PlannedDate ?? x.Date).ToString("dd.MM.yyyy");
                        return string.IsNullOrWhiteSpace(vehicle)
                            ? $"Услуга: {number} от {date}"
                            : $"Услуга: {number} от {date}, {vehicle}";
                    });

        return movements
            .Select(x =>
            {
                var maintenanceId = x.MaintenanceId
                    ?? (string.Equals(x.SourceEntityType, "maintenance", StringComparison.OrdinalIgnoreCase) ? x.SourceEntityId : null);
                var sourceTitle = MaterialInventoryQueryService.ResolveMovementSourceTitle(x, maintenanceId, maintenancesById);
                var sourceUrl = maintenanceId.HasValue ? $"#maintenance/{maintenanceId.Value}" : null;

                return new OrganizationMaterialMovementDto
                {
                    Id = x.Id,
                    StockId = x.StockId,
                    CatalogItemId = x.CatalogItemId,
                    MaintenanceId = x.MaintenanceId,
                    OperationId = x.OperationId,
                    EmployeeId = x.EmployeeId,
                    MemberId = x.MemberId,
                    MaterialName = x.MaterialName,
                    TypeCode = x.TypeCode,
                    Quantity = x.Quantity,
                    BalanceAfter = x.BalanceAfter,
                    UnitCost = x.UnitCost,
                    TotalCost = x.TotalCost,
                    CostBalanceAfter = x.CostBalanceAfter,
                    SourceEntityType = x.SourceEntityType,
                    SourceEntityId = x.SourceEntityId,
                    SourceTitle = sourceTitle,
                    SourceUrl = sourceUrl,
                    CreatedByUserId = x.CreatedByUserId,
                    CreatedByDisplayName = x.CreatedByUserId != null && usersById.TryGetValue(x.CreatedByUserId, out var userName) ? userName : null,
                    Unit = x.Unit,
                    Comment = x.Comment,
                    CreatedAt = x.CreatedAt
                };
            })
            .ToList();
    }

    /// <summary>
    /// Возвращает исторический срез по сохранённым балансам после складских операций.
    /// </summary>
    private async Task<IReadOnlyList<OrganizationMaterialStockSnapshotDto>> BuildSnapshotAsync(Guid organizationId, DateTime snapshotAt)
    {
        var stockRows = await this._context.OrganizationMaterialStocks
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => new
            {
                x.Id,
                x.CatalogItemId,
                x.Name,
                x.Unit,
                x.QuantityOnHand,
                x.AverageUnitCost,
                x.InventoryValue,
                HasMovements = this._context.OrganizationMaterialMovements.Any(movement => movement.StockId == x.Id)
            })
            .ToListAsync();

        var movementRows = await this._context.OrganizationMaterialMovements
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.CreatedAt <= snapshotAt)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new { x.StockId, x.QuantityBalanceAfter, x.CostBalanceAfter })
            .ToListAsync();

        var balancesByStockId = movementRows
            .GroupBy(x => x.StockId)
            .ToDictionary(group => group.Key, group => group.First());

        return stockRows
            .Select(stock =>
            {
                balancesByStockId.TryGetValue(stock.Id, out var balance);
                var quantity = balance?.QuantityBalanceAfter ?? (stock.HasMovements ? 0 : stock.QuantityOnHand);
                var value = balance?.CostBalanceAfter ?? (stock.HasMovements ? 0 : stock.InventoryValue);
                var averageUnitCost = quantity > 0
                    ? Math.Round(value / quantity, 4, MidpointRounding.AwayFromZero)
                    : 0;

                return new OrganizationMaterialStockSnapshotDto
                {
                    StockId = stock.Id,
                    CatalogItemId = stock.CatalogItemId,
                    Name = stock.Name,
                    Unit = InventoryUnitRules.NormalizeStoredUnit(stock.Unit),
                    QuantityOnHand = quantity,
                    AverageUnitCost = averageUnitCost,
                    InventoryValue = Math.Round(value, 2, MidpointRounding.AwayFromZero)
                };
            })
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Unit)
            .ToList();
    }

    /// <summary>
    /// Возвращает понятную причину существования движения по его типу и источнику.
    /// </summary>
    private static string ResolveMovementSourceTitle(
        OrganizationMaterialMovementDto movement,
        Guid? maintenanceId,
        IReadOnlyDictionary<Guid, string> maintenancesById)
    {
        if (maintenanceId.HasValue && maintenancesById.TryGetValue(maintenanceId.Value, out var maintenanceTitle))
        {
            return maintenanceTitle;
        }

        if (string.Equals(movement.SourceEntityType, "manual", StringComparison.OrdinalIgnoreCase))
        {
            return movement.TypeCode switch
            {
                OrganizationMaterialMovementTypeCodes.Receipt => "Ручной приход на склад",
                OrganizationMaterialMovementTypeCodes.Issue => "Ручное списание со склада",
                OrganizationMaterialMovementTypeCodes.Adjustment => "Ручная корректировка остатка",
                _ => "Ручная складская операция"
            };
        }

        if (!string.IsNullOrWhiteSpace(movement.SourceEntityType))
        {
            return $"Источник: {movement.SourceEntityType}";
        }

        return movement.TypeCode switch
        {
            OrganizationMaterialMovementTypeCodes.Receipt => "Приход на склад",
            OrganizationMaterialMovementTypeCodes.Issue => "Списание со склада",
            OrganizationMaterialMovementTypeCodes.MaintenanceIssue => "Списание по услуге",
            OrganizationMaterialMovementTypeCodes.Adjustment => "Корректировка остатка",
            _ => "Складская операция"
        };
    }

    /// <summary>
    /// Приводит дату из query string к UTC, чтобы фильтры склада сравнивались с CreatedAt.
    /// </summary>
    private static DateTime? NormalizeDateTimeUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }
}
