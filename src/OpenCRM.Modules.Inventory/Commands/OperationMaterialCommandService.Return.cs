using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Modules.Inventory.Materials;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Commands;

/// <summary> Возвращает материалы, выданные для работ по обслуживанию. </summary>
internal sealed partial class OperationMaterialCommandService
{
    /// <summary> Возвращает ранее выданный материал по движению. </summary>
    public async Task<OperationMaterialIssueDto> ReverseOperationMaterialAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        Guid movementId,
        ReverseOperationMaterialDto request)
    {
        await access.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkExecute);
        var reason = request.Reason?.Trim();
        if (request.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
        {
            throw new InvalidOperationException("Укажите причину возврата материала не длиннее 1000 символов.");
        }

        return await transaction.ExecuteAsync(
            () => ReturnCoreAsync(userId, organizationId, maintenanceId, detailId, movementId, request, reason),
            () => context.OrganizationMaterialMovements.AsNoTracking().AnyAsync(item => item.CommandId == request.CommandId));
    }

    /// <summary> Выполняет возврат выданного материала по движению. </summary>
    private async Task<OperationMaterialIssueDto> ReturnCoreAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        Guid movementId,
        ReverseOperationMaterialDto request,
        string reason)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Organizations" WHERE "Id" = {organizationId} FOR NO KEY UPDATE""");
        var assignment = await assignmentLoader.LoadAsync(userId, organizationId, maintenanceId, detailId);
        var maintenance = assignment.Maintenance;
        var member = assignment.Member;
        var employee = assignment.Employee;
        var organizationCurrencyCode = await context.Organizations
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.CurrencyCode)
            .SingleAsync();
        MaintenanceCurrencyRules.EnsureMatchesOrganizationCurrency(maintenance, organizationCurrencyCode);
        var original = await context.OrganizationMaterialMovements.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == movementId && item.OrganizationId == organizationId
                && item.MaintenanceId == maintenanceId && item.OperationId == detailId
                && item.TypeCode == OrganizationMaterialMovementTypeCodes.MaintenanceIssue
                && item.SourceEntityType == "operation" && item.Quantity < 0)
            ?? throw new KeyNotFoundException("Списание не найдено.");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "OrganizationMaterialStocks" WHERE "Id" = {original.StockId} AND "OrganizationId" = {organizationId} FOR UPDATE""");
        var stock = await context.OrganizationMaterialStocks.SingleAsync(item => item.Id == original.StockId && item.OrganizationId == organizationId);
        var previousCommand = await context.OrganizationMaterialMovements.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CommandId == request.CommandId);
        if (previousCommand != null)
        {
            if (previousCommand.SourceEntityType != "operation_return"
                || previousCommand.SourceEntityId != movementId || previousCommand.CreatedByUserId != userId
                || previousCommand.Comment != reason)
            {
                throw new InvalidOperationException("Идентификатор запроса уже использован для другого действия.");
            }

            return InventoryDtoMapper.MapOperationIssue(previousCommand, stock);
        }

        if (maintenance.Status != MaintenanceStatus.InProgress || maintenance.ReadyForHandoverAt.HasValue
            || maintenance.HandedOverAt.HasValue)
        {
            throw new InvalidOperationException("Вернуть материал можно только во время выполнения работы.");
        }

        var operation = await context.MaintenanceWorkOperations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == detailId && item.MaintenanceId == maintenanceId);
        if (operation?.Status != MaintenanceWorkStatus.InProgress || operation.EmployeeId != employee.Id)
        {
            throw new InvalidOperationException("Вернуть материал можно только во время своей активной работы.");
        }
        if (await context.OrganizationMaterialMovements.AsNoTracking()
            .AnyAsync(item => item.SourceEntityType == "operation_return" && item.SourceEntityId == movementId))
        {
            throw new InvalidOperationException("Материал уже возвращён.");
        }

        var quantity = -original.Quantity;
        InventoryCostRules.ApplyReceiptCost(stock, quantity, original.UnitCost);
        stock.QuantityOnHand += quantity;
        stock.InventoryValue = InventoryCostRules.CalculateInventoryValue(stock.QuantityOnHand, stock.AverageUnitCost);
        stock.UpdatedAt = DateTime.UtcNow;
        var movement = new OrganizationMaterialMovement
        {
            OrganizationId = organizationId,
            CurrencyCode = stock.CurrencyCode,
            StockId = stock.Id,
            CatalogItemId = stock.CatalogItemId,
            MaintenanceId = maintenanceId,
            OperationId = detailId,
            EmployeeId = employee.Id,
            MemberId = member.Id,
            CommandId = request.CommandId,
            TypeCode = OrganizationMaterialMovementTypeCodes.MaintenanceIssue,
            Quantity = quantity,
            EnteredQuantity = original.EnteredQuantity ?? quantity,
            EnteredUnit = original.EnteredUnit ?? stock.Unit,
            ConversionFactor = original.ConversionFactor ?? 1,
            Unit = stock.Unit,
            UnitCost = original.UnitCost,
            TotalCost = original.TotalCost,
            QuantityBalanceAfter = stock.QuantityOnHand,
            CostBalanceAfter = stock.InventoryValue,
            SourceEntityType = "operation_return",
            SourceEntityId = movementId,
            Comment = reason,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        context.OrganizationMaterialMovements.Add(movement);
        maintenance.MaterialCostAmount -= original.TotalCost;
        await context.SaveChangesAsync();

        return InventoryDtoMapper.MapOperationIssue(movement, stock);
    }
}
