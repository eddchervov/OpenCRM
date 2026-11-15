using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.Queries;

/// <summary> Читает историю работ клиента без ограничения последними записями карточки. </summary>
internal sealed class ClientMaintenancePageQueryService(
    OpenCRMContext context,
    ClientsAccess access,
    IOrganizationPermissionService permissions) : IOrganizationClientMaintenancePageQueryService
{
    /// <summary> Возвращает страницу доступных работ клиента. </summary>
    public async Task<OrganizationClientMaintenancePageDto?> GetPageAsync(
        string userId,
        Guid organizationId,
        Guid clientId,
        int page = 1,
        int pageSize = 10,
        string? searchQuery = null,
        string? status = null)
    {
        await access.EnsureEnabledAsync(userId, organizationId);
        if (!await permissions.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceView))
        {
            throw new UnauthorizedAccessException();
        }

        var clientExists = await context.OrganizationClients.AsNoTracking()
            .AnyAsync(client => client.Id == clientId && client.OrganizationId == organizationId && !client.IsDeleted);
        if (!clientExists)
        {
            return null;
        }

        var query = context.Maintenance.AsNoTracking()
            .Where(maintenance => !maintenance.IsDeleted
                && !maintenance.Vehicle.IsDeleted
                && maintenance.Vehicle.OrganizationId == organizationId
                && maintenance.BuyerClientId == clientId);

        query = status?.Trim() switch
        {
            null or "" => query,
            "upcoming" => query.Where(maintenance => maintenance.Status == MaintenanceStatus.Planned
                || maintenance.Status == MaintenanceStatus.InProgress),
            "completed" => query.Where(maintenance => maintenance.Status == MaintenanceStatus.Completed),
            _ => throw new InvalidOperationException("Неизвестный фильтр статуса работ.")
        };

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var search = searchQuery.Trim();
            var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(maintenance => EF.Functions.ILike(
                    (maintenance.Vehicle.Brand != null ? maintenance.Vehicle.Brand.Name : maintenance.Vehicle.ManualBrandName ?? "")
                    + " " + (maintenance.Vehicle.Model != null ? maintenance.Vehicle.Model.Name : maintenance.Vehicle.ManualModelName ?? "")
                    + " " + (maintenance.Vehicle.Name ?? "")
                    + " " + (maintenance.Vehicle.RegistrationPlate ?? ""), pattern, "\\")
                || EF.Functions.ILike(maintenance.Comment ?? "", pattern, "\\")
                || maintenance.Details.Any(detail => !detail.MaterialCatalogItemId.HasValue
                    && detail.MaterialName == null && !detail.MaterialQuantity.HasValue
                    && EF.Functions.ILike(detail.Name, pattern, "\\")));
        }

        pageSize = Math.Clamp(pageSize, 1, 100);
        var totalCount = await query.CountAsync();
        var lastPage = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, lastPage);
        var rows = await query
            .OrderByDescending(maintenance => maintenance.Status == MaintenanceStatus.Completed
                ? maintenance.CompletedDate ?? maintenance.Date
                : maintenance.PlannedDate ?? maintenance.Date)
            .ThenByDescending(maintenance => maintenance.DateTimeCreation)
            .ThenByDescending(maintenance => maintenance.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(maintenance => maintenance.Details)
            .Include(maintenance => maintenance.Vehicle).ThenInclude(vehicle => vehicle.Brand)
            .Include(maintenance => maintenance.Vehicle).ThenInclude(vehicle => vehicle.Model)
            .ToListAsync();

        var canViewFinance = await permissions.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.FinanceView)
            || await permissions.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.FinanceManage);
        var pageIds = rows.Select(maintenance => maintenance.Id).ToArray();
        var payments = canViewFinance && pageIds.Length > 0
            ? await context.MaintenancePayments.AsNoTracking()
                .Where(payment => !payment.CancelledAt.HasValue && pageIds.Contains(payment.MaintenanceId))
                .GroupBy(payment => payment.MaintenanceId)
                .Select(group => new { Id = group.Key, Amount = group.Sum(payment => payment.Amount) })
                .ToDictionaryAsync(payment => payment.Id, payment => payment.Amount)
            : new Dictionary<Guid, decimal>();

        return new OrganizationClientMaintenancePageDto
        {
            Items = rows.Select(maintenance => ClientMaintenanceMapper.Map(
                maintenance, canViewFinance ? payments.GetValueOrDefault(maintenance.Id) : null, canViewFinance, true)).ToArray(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }
}
