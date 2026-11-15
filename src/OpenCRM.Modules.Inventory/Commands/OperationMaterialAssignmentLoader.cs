using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Loading;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Inventory.Commands;

/// <summary> Загружает контекст обслуживания и сотрудника для операций с материалами. </summary>
internal sealed class OperationMaterialAssignmentLoader(
    OpenCRMContext context,
    IOrganizationSubscriptionService access)
{
    /// <summary> Загружает запись обслуживания и сотрудника для выдачи материалов. </summary>
    internal async Task<OperationMaterialAssignment> LoadAsync(string userId, Guid organizationId, Guid maintenanceId, Guid detailId)
    {
        var maintenance = await context.Maintenance
            .SingleOrDefaultAsync(item => item.Id == maintenanceId
                && item.Vehicle.OrganizationId == organizationId && !item.IsDeleted)
            ?? throw new KeyNotFoundException("Обслуживание не найдено.");
        await context.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Maintenance" WHERE "Id" = {maintenanceId} FOR UPDATE""");
        await context.Entry(maintenance).ReloadAsync();
        await MaintenanceDetailsLoader.ReloadAsync(context, maintenance);

        var member = await context.OrganizationMembers.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.UserId == userId)
            ?? throw new UnauthorizedAccessException("Участник организации не найден.");
        var employee = await context.OrganizationEmployees.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.MemberId == member.Id && item.IsActive)
            ?? throw new UnauthorizedAccessException("Аккаунт не связан с действующим сотрудником.");
        await context.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "OrganizationEmployees" WHERE "Id" = {employee.Id} FOR UPDATE""");
        employee = await context.OrganizationEmployees.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.MemberId == member.Id && item.IsActive)
            ?? throw new UnauthorizedAccessException("Аккаунт не связан с действующим сотрудником.");
        await access.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkExecute);

        var detail = maintenance.Details.SingleOrDefault(item => item.DetailId == detailId)
            ?? throw new KeyNotFoundException("Работа не найдена.");
        if (detail.MaterialCatalogItemId.HasValue || detail.MaterialName != null || detail.MaterialQuantity.HasValue)
        {
            throw new KeyNotFoundException("Работа не найдена.");
        }
        if ((detail.EmployeeId ?? maintenance.EmployeeId) != employee.Id)
        {
            throw new UnauthorizedAccessException("Работа не назначена этому исполнителю.");
        }

        return new OperationMaterialAssignment(maintenance, member, employee);
    }
}
