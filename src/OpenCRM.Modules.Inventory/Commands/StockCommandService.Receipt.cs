using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Modules.Inventory.Materials;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Commands;

/// <summary> Оформляет приёмку материалов на склад организации. </summary>
internal sealed partial class StockCommandService(
    OpenCRMContext context,
    IOrganizationSubscriptionService access,
    InventoryCommandTransaction transaction,
    InventoryStockRepository stocks) : IStockCommandService
{
    /// <summary> Максимально допустимое количество в строке приёмки. </summary>
    private const int MaxQuantity = 999_999_999;

    /// <summary> Оформляет приёмку материала на склад. </summary>
    public async Task<OrganizationMaterialStockDto> AddReceiptAsync(string userId, Guid organizationId, CreateOrganizationMaterialReceiptDto request)
    {
        await access.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.MaterialsManage);
        ValidateReceipt(request);
        var movementId = Guid.NewGuid();

        return await transaction.ExecuteAsync(
            () => AddReceiptCoreAsync(userId, organizationId, request, movementId),
            () => context.OrganizationMaterialMovements.AsNoTracking().AnyAsync(item => item.Id == movementId));
    }

    /// <summary> Выполняет приёмку материала на склад. </summary>
    private async Task<OrganizationMaterialStockDto> AddReceiptCoreAsync(
        string userId,
        Guid organizationId,
        CreateOrganizationMaterialReceiptDto request,
        Guid movementId)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Organizations" WHERE "Id" = {organizationId} FOR NO KEY UPDATE""");
        var material = await stocks.ResolveReceiptMaterialAsync(organizationId, request.CatalogItemId, request.Name, request.Unit);
        ValidateMaterialName(material.Name);
        var stock = await stocks.GetOrCreateAsync(organizationId, material.CatalogItemId, material.Name, InventoryUnitRules.ToBaseUnit(material.Unit));
        if (context.Entry(stock).State != EntityState.Added)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "OrganizationMaterialStocks" WHERE "Id" = {stock.Id} FOR UPDATE""");
            await context.Entry(stock).ReloadAsync();
        }

        var quantity = InventoryUnitRules.ConvertQuantity(request.Quantity, material.Unit, stock.Unit);
        if (request.PackageQuantity.HasValue)
        {
            var packageQuantity = InventoryUnitRules.ConvertQuantity(request.PackageQuantity.Value, InventoryUnitRules.ToBaseUnit(material.Unit), stock.Unit);
            InventoryUnitRules.EnsureDiscreteQuantity(stock.Unit, packageQuantity, "Количество в упаковке");
            if (stock.PackageQuantity.HasValue && stock.PackageQuantity != packageQuantity)
            {
                throw new InvalidOperationException("Коэффициент упаковки материала уже закреплен и не может быть изменен приходом.");
            }

            stock.PackageQuantity = packageQuantity;
        }
        if (quantity > MaxQuantity)
        {
            throw new InvalidOperationException("Количество прихода слишком большое.");
        }
        InventoryUnitRules.EnsureDiscreteQuantity(stock.Unit, quantity, "Количество прихода");
        var unitCost = Math.Round(InventoryCostRules.NormalizeMoney(request.UnitCost) * request.Quantity / quantity, 8, MidpointRounding.AwayFromZero);
        InventoryCostRules.ApplyReceiptCost(stock, quantity, unitCost);
        stock.QuantityOnHand += quantity;
        stock.InventoryValue = InventoryCostRules.CalculateInventoryValue(stock.QuantityOnHand, stock.AverageUnitCost);
        stock.UpdatedAt = DateTime.UtcNow;
        context.OrganizationMaterialMovements.Add(new OrganizationMaterialMovement
        {
            Id = movementId,
            OrganizationId = organizationId,
            Stock = stock,
            CatalogItemId = material.CatalogItemId,
            TypeCode = OrganizationMaterialMovementTypeCodes.Receipt,
            Quantity = quantity,
            EnteredQuantity = request.Quantity,
            EnteredUnit = material.Unit,
            ConversionFactor = quantity / request.Quantity,
            Unit = stock.Unit,
            UnitCost = unitCost,
            TotalCost = InventoryCostRules.CalculateMovementCost(quantity, unitCost),
            QuantityBalanceAfter = stock.QuantityOnHand,
            CostBalanceAfter = stock.InventoryValue,
            SourceEntityType = "manual",
            Comment = InventoryDtoMapper.NormalizeComment(request.Comment) ?? "Приход на склад",
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        return InventoryDtoMapper.MapStock(stock);
    }

    /// <summary> Проверяет корректность количества в упаковке. </summary>
    private static void ValidatePackageQuantity(decimal? packageQuantity)
    {
        if (packageQuantity.HasValue && (packageQuantity <= 0 || packageQuantity > MaxQuantity
            || packageQuantity != decimal.Round(packageQuantity.Value, 3)))
        {
            throw new InvalidOperationException("Укажите положительное количество в упаковке с точностью до трех знаков.");
        }
    }
}
