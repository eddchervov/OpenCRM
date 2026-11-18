using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Materials;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Commands;

/// <summary> Синхронизирует расход материалов при обслуживании. </summary>
internal sealed class MaintenanceConsumptionCommandService(
    OpenCRMContext context,
    IOrganizationSubscriptionService access,
    InventoryCommandTransaction transaction,
    InventoryStockRepository stocks) : IMaintenanceConsumptionService
{
    /// <summary> Максимально допустимое количество в строке расхода. </summary>
    private const int MaxQuantity = 999_999_999;

    /// <summary> Синхронизирует строки расхода материалов по обслуживанию. </summary>
    public async Task<MaintenanceConsumptionResult> SynchronizeAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        IReadOnlyCollection<MaintenanceMaterialConsumptionLine> details)
    {
        await access.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);
        var movementIds = new List<Guid>();

        return await transaction.ExecuteAsync(
            () => SynchronizeCoreAsync(userId, organizationId, maintenanceId, details, movementIds),
            async () => movementIds.Count > 0 && await context.OrganizationMaterialMovements.AsNoTracking()
                .CountAsync(movement => movementIds.Contains(movement.Id)) == movementIds.Count);
    }

    /// <summary> Выполняет синхронизацию расхода материалов по обслуживанию. </summary>
    private async Task<MaintenanceConsumptionResult> SynchronizeCoreAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        IReadOnlyCollection<MaintenanceMaterialConsumptionLine> details,
        List<Guid> movementIds)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Organizations" WHERE "Id" = {organizationId} FOR NO KEY UPDATE""");
        await context.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Maintenance" WHERE "Id" = {maintenanceId} FOR UPDATE""");
        var maintenance = await context.Maintenance.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == maintenanceId && item.Vehicle.OrganizationId == organizationId)
            ?? throw new KeyNotFoundException("Обслуживание не найдено.");
        var organizationCurrencyCode = await context.Organizations
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.CurrencyCode)
            .SingleAsync();
        MaintenanceCurrencyRules.EnsureMatchesOrganizationCurrency(maintenance, organizationCurrencyCode);

        var previousMovements = await context.OrganizationMaterialMovements
            .AsNoTracking()
            .Where(movement => movement.OrganizationId == organizationId
                && movement.MaintenanceId == maintenanceId
                && movement.TypeCode == OrganizationMaterialMovementTypeCodes.MaintenanceIssue
                && movement.OperationId == null)
            .ToListAsync();
        var desiredQuantities = new Dictionary<Guid, decimal>();
        var stockRows = new Dictionary<Guid, OrganizationMaterialStock>();
        var detailRows = new List<(MaintenanceMaterialConsumptionLine Detail, Guid StockId, decimal Quantity)>();
        foreach (var detail in details)
        {
            if (!detail.Quantity.HasValue || detail.Quantity.Value <= 0)
            {
                continue;
            }
            if (detail.Quantity.Value > MaxQuantity)
            {
                throw new InvalidOperationException("Расход материала слишком большой.");
            }

            var material = await stocks.ResolveMaterialAsync(
                organizationId,
                detail.CatalogItemId,
                detail.MaterialName ?? detail.Name,
                detail.Unit);
            var stock = await stocks.GetOrCreateAsync(
                organizationId,
                material.CatalogItemId,
                material.Name,
                InventoryUnitRules.ToBaseUnit(material.Unit));
            var quantity = InventoryUnitRules.ConvertQuantity(detail.Quantity.Value, material.Unit, stock.Unit);
            if (quantity > MaxQuantity)
            {
                throw new InvalidOperationException("Расход материала слишком большой.");
            }
            InventoryUnitRules.EnsureDiscreteQuantity(stock.Unit, quantity, "Расход материала");
            stockRows[stock.Id] = stock;
            desiredQuantities[stock.Id] = desiredQuantities.GetValueOrDefault(stock.Id) + quantity;
            detailRows.Add((detail, stock.Id, quantity));
        }

        var newMovements = new List<OrganizationMaterialMovement>();
        var stockIds = previousMovements.Select(movement => movement.StockId)
            .Concat(stockRows.Keys).Distinct().OrderBy(id => id).ToArray();
        foreach (var stockId in stockIds)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "OrganizationMaterialStocks" WHERE "Id" = {stockId} FOR UPDATE""");
            if (!stockRows.TryGetValue(stockId, out var stock))
            {
                stock = await context.OrganizationMaterialStocks.SingleAsync(item => item.Id == stockId);
            }
            else if (context.Entry(stock).State != EntityState.Added)
            {
                await context.Entry(stock).ReloadAsync();
            }

            var previousQuantity = -previousMovements.Where(movement => movement.StockId == stockId)
                .Sum(movement => movement.Quantity);
            var desiredQuantity = desiredQuantities.GetValueOrDefault(stockId);
            var quantityDelta = previousQuantity - desiredQuantity;
            if (quantityDelta == 0)
            {
                continue;
            }
            if (quantityDelta < 0 && -quantityDelta > stock.QuantityOnHand - stock.ReservedQuantity)
            {
                throw new InvalidOperationException(
                    $"Недостаточно материала \"{stock.Name}\" на складе: доступно {InventoryDtoMapper.FormatQuantity(stock.QuantityOnHand - stock.ReservedQuantity)} {stock.Unit}.");
            }

            var previousCost = previousMovements.Where(movement => movement.StockId == stockId)
                .Sum(movement => movement.Quantity < 0 ? movement.TotalCost : -movement.TotalCost);
            var unitCost = quantityDelta > 0 && previousQuantity > 0
                ? Math.Round(previousCost / previousQuantity, 8, MidpointRounding.AwayFromZero)
                : stock.AverageUnitCost;
            if (quantityDelta > 0)
            {
                InventoryCostRules.ApplyReceiptCost(stock, quantityDelta, unitCost);
            }

            stock.QuantityOnHand += quantityDelta;
            stock.InventoryValue = InventoryCostRules.CalculateInventoryValue(stock.QuantityOnHand, stock.AverageUnitCost);
            stock.UpdatedAt = DateTime.UtcNow;
            var movement = new OrganizationMaterialMovement
            {
                OrganizationId = organizationId,
                CurrencyCode = stock.CurrencyCode,
                StockId = stock.Id,
                CatalogItemId = stock.CatalogItemId,
                MaintenanceId = maintenanceId,
                TypeCode = OrganizationMaterialMovementTypeCodes.MaintenanceIssue,
                Quantity = quantityDelta,
                EnteredQuantity = Math.Abs(quantityDelta),
                EnteredUnit = stock.Unit,
                ConversionFactor = 1,
                Unit = stock.Unit,
                UnitCost = unitCost,
                TotalCost = InventoryCostRules.CalculateMovementCost(quantityDelta, unitCost),
                QuantityBalanceAfter = stock.QuantityOnHand,
                CostBalanceAfter = stock.InventoryValue,
                SourceEntityType = "maintenance",
                SourceEntityId = maintenanceId,
                Comment = quantityDelta > 0 ? "Возврат расхода после изменения работ" : "Списание по обслуживанию",
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };
            context.OrganizationMaterialMovements.Add(movement);
            newMovements.Add(movement);
        }

        var allAutomaticMovements = previousMovements.Concat(newMovements).ToList();
        var costByStock = allAutomaticMovements.GroupBy(movement => movement.StockId)
            .ToDictionary(group => group.Key, group => group.Sum(movement => movement.Quantity < 0
                ? movement.TotalCost : -movement.TotalCost));
        var remainingCostByStock = costByStock.ToDictionary(item => item.Key, item => item.Value);
        var remainingRowsByStock = detailRows.GroupBy(row => row.StockId)
            .ToDictionary(group => group.Key, group => group.Count());
        var detailCosts = new List<MaintenanceMaterialConsumptionCost>(detailRows.Count);
        foreach (var (detail, stockId, quantity) in detailRows)
        {
            var detailCost = remainingRowsByStock[stockId] == 1
                ? remainingCostByStock[stockId]
                : Math.Round(costByStock[stockId] * quantity / desiredQuantities[stockId], 2, MidpointRounding.AwayFromZero);
            remainingRowsByStock[stockId]--;
            remainingCostByStock[stockId] -= detailCost;
            detailCosts.Add(new MaintenanceMaterialConsumptionCost(
                detail.DetailId,
                Math.Round(detailCost / detail.Quantity!.Value, 4, MidpointRounding.AwayFromZero),
                detailCost));
        }

        var materialCostAmount = costByStock.Values.Sum();
        movementIds.Clear();
        movementIds.AddRange(newMovements.Select(movement => movement.Id));
        await context.SaveChangesAsync();
        var operationMovements = await context.OrganizationMaterialMovements.AsNoTracking()
            .Where(movement => movement.OrganizationId == organizationId
                && movement.MaintenanceId == maintenanceId && movement.OperationId != null)
            .ToListAsync();
        var operationCostByDetail = operationMovements
            .Where(movement => movement.OperationId.HasValue)
            .GroupBy(movement => movement.OperationId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(movement => movement.Quantity < 0 ? movement.TotalCost : -movement.TotalCost));
        var operationCostAmount = operationCostByDetail.Values.Sum();
        var costsByDetailId = detailCosts.ToDictionary(cost => cost.DetailId);
        foreach (var line in details)
        {
            if (!operationCostByDetail.TryGetValue(line.DetailId, out var detailCost))
            {
                continue;
            }

            costsByDetailId[line.DetailId] = new MaintenanceMaterialConsumptionCost(line.DetailId, null, detailCost);
        }

        return new MaintenanceConsumptionResult(
            Math.Round(materialCostAmount + operationCostAmount, 2, MidpointRounding.AwayFromZero),
            costsByDetailId.Values.ToArray());
    }
}
