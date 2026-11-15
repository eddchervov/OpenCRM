using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Modules.Inventory.Materials;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory;

/// <summary> Читает доступные и выданные материалы для работ по обслуживанию. </summary>
internal sealed class OperationMaterialQueryService(
    OpenCRMContext context,
    IOrganizationSubscriptionService access) : IOperationMaterialQueryService
{
    /// <summary> Возвращает материалы со склада, доступные для строки работы. </summary>
    public async Task<IReadOnlyList<OperationMaterialStockDto>> GetAvailableForOperationAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId)
    {
        await access.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkView);
        var memberId = await context.OrganizationMembers.AsNoTracking()
            .Where(member => member.OrganizationId == organizationId && member.UserId == userId)
            .Select(member => (Guid?)member.Id)
            .SingleOrDefaultAsync()
            ?? throw new UnauthorizedAccessException("Участник организации не найден.");
        var employeeId = await context.OrganizationEmployees.AsNoTracking()
            .Where(employee => employee.OrganizationId == organizationId
                && employee.MemberId == memberId && employee.IsActive)
            .Select(employee => (Guid?)employee.Id)
            .SingleOrDefaultAsync()
            ?? throw new UnauthorizedAccessException("Аккаунт не связан с действующим сотрудником.");
        var maintenance = await context.Maintenance.AsNoTracking()
            .Include(item => item.Details)
            .SingleOrDefaultAsync(item => item.Id == maintenanceId
                && item.Vehicle.OrganizationId == organizationId && !item.IsDeleted)
            ?? throw new KeyNotFoundException("Обслуживание не найдено.");
        var detail = maintenance.Details.SingleOrDefault(item => item.DetailId == detailId)
            ?? throw new KeyNotFoundException("Работа не найдена.");
        if (detail.MaterialCatalogItemId.HasValue || detail.MaterialName != null || detail.MaterialQuantity.HasValue)
        {
            throw new KeyNotFoundException("Работа не найдена.");
        }
        if ((detail.EmployeeId ?? maintenance.EmployeeId) != employeeId)
        {
            throw new UnauthorizedAccessException("Работа не назначена этому исполнителю.");
        }

        var stocks = await context.OrganizationMaterialStocks.AsNoTracking()
            .Where(stock => stock.OrganizationId == organizationId
                && stock.QuantityOnHand > stock.ReservedQuantity)
            .OrderBy(stock => stock.Name)
            .Select(stock => new
            {
                stock.Id,
                stock.Name,
                stock.Unit,
                stock.PackageQuantity,
                AvailableQuantity = stock.QuantityOnHand - stock.ReservedQuantity
            })
            .ToListAsync();

        return stocks.Select(stock => new OperationMaterialStockDto
        {
            Id = stock.Id,
            Name = stock.Name,
            Unit = InventoryUnitRules.NormalizeStoredUnit(stock.Unit),
            PackageQuantity = stock.PackageQuantity,
            AvailableQuantity = stock.AvailableQuantity
        }).ToList();
    }

    /// <summary> Возвращает материалы, выданные для строки работы. </summary>
    public async Task<IReadOnlyList<OperationMaterialIssueDto>> GetIssuedForOperationAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId)
    {
        await this.GetAvailableForOperationAsync(userId, organizationId, maintenanceId, detailId);
        var movements = await context.OrganizationMaterialMovements.AsNoTracking()
            .Where(movement => movement.OrganizationId == organizationId
                && movement.MaintenanceId == maintenanceId && movement.OperationId == detailId)
            .OrderByDescending(movement => movement.CreatedAt)
            .Select(movement => new
            {
                movement.Id,
                movement.StockId,
                MaterialName = movement.Stock.Name,
                Quantity = Math.Abs(movement.Quantity),
                movement.Unit,
                movement.CreatedAt,
                IsReturn = movement.Quantity > 0,
                SourceMovementId = movement.SourceEntityType == "operation_return" ? movement.SourceEntityId : null
            })
            .ToListAsync();

        return movements.Select(movement => new OperationMaterialIssueDto
        {
            Id = movement.Id,
            StockId = movement.StockId,
            MaterialName = movement.MaterialName,
            Quantity = movement.Quantity,
            Unit = InventoryUnitRules.NormalizeStoredUnit(movement.Unit),
            CreatedAt = movement.CreatedAt,
            IsReturn = movement.IsReturn,
            SourceMovementId = movement.SourceMovementId
        }).ToList();
    }
}
