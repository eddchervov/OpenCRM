using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Modules.Inventory.Materials;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Commands;

/// <summary> Корректирует складские остатки материалов. </summary>
internal sealed partial class StockCommandService
{
    /// <summary> Корректирует остаток материала на складе. </summary>
    public async Task<OrganizationMaterialStockDto> AdjustAsync(string userId, Guid organizationId, Guid stockId, AdjustOrganizationMaterialStockDto request)
    {
        await access.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.MaterialsManage);
        ValidateAdjustment(request);

        var movementId = Guid.NewGuid();
        return await transaction.ExecuteAsync(
            () => AdjustCoreAsync(userId, organizationId, stockId, request, movementId),
            () => context.OrganizationMaterialMovements.AsNoTracking().AnyAsync(item => item.Id == movementId));
    }

    /// <summary> Выполняет корректировку остатка материала. </summary>
    private async Task<OrganizationMaterialStockDto> AdjustCoreAsync(
        string userId,
        Guid organizationId,
        Guid stockId,
        AdjustOrganizationMaterialStockDto request,
        Guid movementId)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Organizations" WHERE "Id" = {organizationId} FOR NO KEY UPDATE""");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "OrganizationMaterialStocks" WHERE "Id" = {stockId} AND "OrganizationId" = {organizationId} FOR UPDATE""");
        var stock = await context.OrganizationMaterialStocks
            .FirstOrDefaultAsync(item => item.OrganizationId == organizationId && item.Id == stockId)
            ?? throw new InvalidOperationException("Материал не найден.");
        await context.Entry(stock).ReloadAsync();
        InventoryUnitRules.EnsureDiscreteQuantity(stock.Unit, request.QuantityOnHand, "Остаток");
        if (request.QuantityOnHand < stock.ReservedQuantity)
        {
            throw new InvalidOperationException("Остаток не может быть меньше зарезервированного количества.");
        }

        // PostgreSQL сохраняет время с точностью до микросекунды, а ответ команды может содержать более точное значение.
        if (request.ExpectedUpdatedAt!.Value.Ticks / TimeSpan.TicksPerMicrosecond
            != stock.UpdatedAt.Ticks / TimeSpan.TicksPerMicrosecond)
        {
            throw new InvalidOperationException("Остаток уже изменён. Обновите страницу перед корректировкой.");
        }

        var reason = request.Comment!.Trim();
        var delta = request.QuantityOnHand - stock.QuantityOnHand;
        var previousInventoryValue = stock.InventoryValue;
        var averageUnitCost = request.AverageUnitCost ?? stock.AverageUnitCost;
        var description = $"{reason}\nОстаток: {InventoryDtoMapper.FormatQuantity(stock.QuantityOnHand)} -> {InventoryDtoMapper.FormatQuantity(request.QuantityOnHand)} {stock.Unit}."
            + $"\nСебестоимость единицы: {stock.AverageUnitCost} -> {averageUnitCost} {stock.CurrencyCode}.";
        stock.QuantityOnHand = request.QuantityOnHand;
        stock.MinQuantity = Math.Max(0, request.MinQuantity ?? stock.MinQuantity);
        stock.AverageUnitCost = averageUnitCost;
        stock.InventoryValue = InventoryCostRules.CalculateInventoryValue(stock.QuantityOnHand, stock.AverageUnitCost);
        stock.UpdatedAt = DateTime.UtcNow;
        context.OrganizationMaterialMovements.Add(new OrganizationMaterialMovement
        {
            Id = movementId,
            OrganizationId = organizationId,
            CurrencyCode = stock.CurrencyCode,
            StockId = stock.Id,
            CatalogItemId = stock.CatalogItemId,
            TypeCode = OrganizationMaterialMovementTypeCodes.Adjustment,
            Quantity = delta,
            Unit = stock.Unit,
            UnitCost = stock.AverageUnitCost,
            TotalCost = Math.Abs(stock.InventoryValue - previousInventoryValue),
            QuantityBalanceAfter = stock.QuantityOnHand,
            CostBalanceAfter = stock.InventoryValue,
            SourceEntityType = "manual",
            Comment = description,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        return InventoryDtoMapper.MapStock(stock);
    }
}
