using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Modules.Inventory.Materials;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Commands;

/// <summary> Выдаёт и возвращает материалы для работ по обслуживанию. </summary>
internal sealed partial class OperationMaterialCommandService(
    OpenCRMContext context,
    IOrganizationSubscriptionService access,
    InventoryCommandTransaction transaction,
    OperationMaterialAssignmentLoader assignmentLoader) : IOperationMaterialCommandService
{
    /// <summary> Максимально допустимое количество выдаваемого материала. </summary>
    private const int MaxQuantity = 999_999_999;

    /// <summary> Выдаёт материал для строки работы по обслуживанию. </summary>
    public async Task<OperationMaterialIssueDto> IssueForOperationAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        IssueOperationMaterialDto request)
    {
        await access.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkExecute);
        if (request.CommandId == Guid.Empty || request.StockId == Guid.Empty || detailId == Guid.Empty
            || request.Quantity <= 0 || request.Quantity > MaxQuantity
            || request.Quantity != decimal.Round(request.Quantity, 3))
        {
            throw new InvalidOperationException("Укажите материал, положительное количество и идентификатор запроса.");
        }

        return await transaction.ExecuteAsync(
            () => IssueCoreAsync(userId, organizationId, maintenanceId, detailId, request),
            () => context.OrganizationMaterialMovements.AsNoTracking().AnyAsync(item => item.CommandId == request.CommandId));
    }

    /// <summary> Выполняет выдачу материала для строки работы. </summary>
    private async Task<OperationMaterialIssueDto> IssueCoreAsync(
        string userId,
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        IssueOperationMaterialDto request)
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
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "OrganizationMaterialStocks" WHERE "Id" = {request.StockId} AND "OrganizationId" = {organizationId} FOR UPDATE""");
        var stock = await context.OrganizationMaterialStocks
            .SingleOrDefaultAsync(item => item.Id == request.StockId && item.OrganizationId == organizationId)
            ?? throw new KeyNotFoundException("Материал не найден.");
        var normalizedQuantity = InventoryUnitRules.ConvertQuantity(request.Quantity, request.Unit, stock.Unit, stock.PackageQuantity);
        var enteredUnit = string.IsNullOrWhiteSpace(request.Unit) ? stock.Unit
            : request.Unit == "уп" ? "уп" : InventoryUnitRules.NormalizeUnit(request.Unit);
        if (normalizedQuantity > MaxQuantity)
        {
            throw new InvalidOperationException("Количество выдачи слишком большое.");
        }
        InventoryUnitRules.EnsureDiscreteQuantity(stock.Unit, normalizedQuantity, "Количество выдачи");

        var previousMovement = await context.OrganizationMaterialMovements.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CommandId == request.CommandId);
        if (previousMovement != null)
        {
            if (previousMovement.OrganizationId != organizationId || previousMovement.MaintenanceId != maintenanceId
                || previousMovement.OperationId != detailId || previousMovement.StockId != request.StockId
                || previousMovement.EmployeeId != employee.Id || previousMovement.MemberId != member.Id
                || previousMovement.Quantity != -normalizedQuantity)
            {
                throw new InvalidOperationException("Идентификатор запроса уже использован для другой выдачи.");
            }

            return InventoryDtoMapper.MapOperationIssue(previousMovement, stock);
        }

        if (maintenance.Status != MaintenanceStatus.InProgress || maintenance.HandedOverAt.HasValue
            || maintenance.ReadyForHandoverAt.HasValue)
        {
            throw new InvalidOperationException("Выдача материала доступна только во время выполнения работы.");
        }

        var operation = await context.MaintenanceWorkOperations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == detailId && item.MaintenanceId == maintenanceId);
        if (operation?.Status != MaintenanceWorkStatus.InProgress || operation.EmployeeId != employee.Id)
        {
            throw new InvalidOperationException("Перед выдачей материала начните работу.");
        }
        if (normalizedQuantity > stock.QuantityOnHand - stock.ReservedQuantity)
        {
            throw new InvalidOperationException($"Недостаточно материала \"{stock.Name}\": доступно {InventoryDtoMapper.FormatQuantity(stock.QuantityOnHand - stock.ReservedQuantity)} {stock.Unit}.");
        }

        var now = DateTime.UtcNow;
        stock.QuantityOnHand -= normalizedQuantity;
        stock.InventoryValue = InventoryCostRules.CalculateInventoryValue(stock.QuantityOnHand, stock.AverageUnitCost);
        stock.UpdatedAt = now;
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
            Quantity = -normalizedQuantity,
            EnteredQuantity = request.Quantity,
            EnteredUnit = enteredUnit,
            ConversionFactor = normalizedQuantity / request.Quantity,
            Unit = stock.Unit,
            UnitCost = stock.AverageUnitCost,
            TotalCost = InventoryCostRules.CalculateMovementCost(normalizedQuantity, stock.AverageUnitCost),
            QuantityBalanceAfter = stock.QuantityOnHand,
            CostBalanceAfter = stock.InventoryValue,
            SourceEntityType = "operation",
            SourceEntityId = detailId,
            Comment = InventoryDtoMapper.NormalizeComment(request.Comment),
            CreatedByUserId = userId,
            CreatedAt = now
        };
        context.OrganizationMaterialMovements.Add(movement);
        maintenance.MaterialCostAmount += movement.TotalCost;
        await context.SaveChangesAsync();

        return InventoryDtoMapper.MapOperationIssue(movement, stock);
    }
}
