using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Modules.Inventory.Materials;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Commands;

/// <summary> Выдаёт материалы со склада организации. </summary>
internal sealed partial class StockCommandService
{
    /// <summary> Оформляет выдачу материала со склада. </summary>
    public async Task<OrganizationMaterialStockDto> IssueAsync(string userId, Guid organizationId, CreateOrganizationMaterialIssueDto request)
    {
        await access.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.MaterialsManage);
        ValidateIssue(request);

        var movementId = Guid.NewGuid();
        return await transaction.ExecuteAsync(
            () => IssueCoreAsync(userId, organizationId, request, movementId),
            () => context.OrganizationMaterialMovements.AsNoTracking().AnyAsync(item => item.Id == movementId));
    }

    /// <summary> Выполняет выдачу материала со склада. </summary>
    private async Task<OrganizationMaterialStockDto> IssueCoreAsync(
        string userId,
        Guid organizationId,
        CreateOrganizationMaterialIssueDto request,
        Guid movementId)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Organizations" WHERE "Id" = {organizationId} FOR NO KEY UPDATE""");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "OrganizationMaterialStocks" WHERE "Id" = {request.StockId} AND "OrganizationId" = {organizationId} FOR UPDATE""");
        var stock = await context.OrganizationMaterialStocks
            .FirstOrDefaultAsync(item => item.OrganizationId == organizationId && item.Id == request.StockId)
            ?? throw new InvalidOperationException("Материал не найден.");
        InventoryUnitRules.EnsureDiscreteQuantity(stock.Unit, request.Quantity, "Количество списания");
        if (request.Quantity > stock.QuantityOnHand - stock.ReservedQuantity)
        {
            throw new InvalidOperationException($"Нельзя списать больше доступного остатка: сейчас {InventoryDtoMapper.FormatQuantity(stock.QuantityOnHand - stock.ReservedQuantity)} {stock.Unit}.");
        }

        var unitCost = stock.AverageUnitCost;
        stock.QuantityOnHand -= request.Quantity;
        stock.InventoryValue = InventoryCostRules.CalculateInventoryValue(stock.QuantityOnHand, stock.AverageUnitCost);
        stock.UpdatedAt = DateTime.UtcNow;
        context.OrganizationMaterialMovements.Add(new OrganizationMaterialMovement
        {
            Id = movementId,
            OrganizationId = organizationId,
            CurrencyCode = stock.CurrencyCode,
            StockId = stock.Id,
            CatalogItemId = stock.CatalogItemId,
            TypeCode = OrganizationMaterialMovementTypeCodes.Issue,
            Quantity = -request.Quantity,
            EnteredQuantity = request.Quantity,
            EnteredUnit = stock.Unit,
            ConversionFactor = 1,
            Unit = stock.Unit,
            UnitCost = unitCost,
            TotalCost = InventoryCostRules.CalculateMovementCost(request.Quantity, unitCost),
            QuantityBalanceAfter = stock.QuantityOnHand,
            CostBalanceAfter = stock.InventoryValue,
            SourceEntityType = "manual",
            Comment = InventoryDtoMapper.NormalizeComment(request.Comment) ?? "Ручное списание",
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        return InventoryDtoMapper.MapStock(stock);
    }
}
