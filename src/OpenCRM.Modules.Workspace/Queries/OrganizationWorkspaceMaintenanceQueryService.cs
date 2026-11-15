using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Workspace.Queries;

/// <summary> Сервис ленты обслуживаний рабочего пространства. </summary>
public sealed class OrganizationWorkspaceMaintenanceQueryService(
    OpenCRMContext opencrmContext) : IOrganizationWorkspaceMaintenanceQueryService
{
    /// <summary> Возвращает постраничную ленту обслуживаний организации за период. </summary>
    public async Task<OrganizationWorkspaceMaintenanceFeedResult> GetMaintenancesAsync(
        Guid organizationId,
        DateTime dateFrom,
        DateTime dateTo,
        int page,
        int pageSize)
    {
        var from = dateFrom.Date;
        var to = dateTo.Date;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var toExclusive = to.AddDays(1);
        var query = opencrmContext.Maintenance
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Vehicle.OrganizationId == organizationId && !x.Vehicle.IsDeleted)
            .Where(x =>
                x.Status == MaintenanceStatus.InProgress ||
                (x.Status == MaintenanceStatus.Planned && ((x.PlannedEndDate ?? x.PlannedDate ?? x.Date).Date > from
                    || ((x.PlannedEndDate ?? x.PlannedDate ?? x.Date).Date == from
                        && (!x.PlannedEndTime.HasValue || x.PlannedEndTime > TimeSpan.Zero))) && (x.PlannedDate ?? x.Date) < toExclusive))
            .OrderByDescending(x => x.Status == MaintenanceStatus.InProgress)
            .ThenBy(x => x.PlannedDate ?? x.Date)
            .ThenByDescending(x => x.DateTimeCreation);

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var normalizedPage = Math.Min(Math.Max(1, page), totalPages);

        var recentMaintenances = await query
            .Include(x => x.Vehicle)
                .ThenInclude(x => x.Brand)
            .Include(x => x.Vehicle)
                .ThenInclude(x => x.Model)
            .Include(x => x.Vehicle)
                .ThenInclude(x => x.OrganizationClient)
            .Skip((normalizedPage - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new OrganizationWorkspaceMaintenanceFeedResult
        {
            Items = recentMaintenances.Select(x => OrganizationWorkspaceMaintenanceMapper.Map(x)).ToList(),
            Page = normalizedPage,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    /// <summary> Возвращает обслуживаний организации для календаря за период. </summary>
    public async Task<IReadOnlyList<OrganizationRecentMaintenanceDto>> GetMaintenanceCalendarAsync(
        Guid organizationId,
        DateTime dateFrom,
        DateTime dateTo,
        Guid? vehicleId = null,
        bool includeAllStatuses = false)
    {
        var from = dateFrom.Date;
        var to = dateTo.Date;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var toExclusive = to.AddDays(1);
        var query = opencrmContext.Maintenance
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Vehicle.OrganizationId == organizationId && !x.Vehicle.IsDeleted)
            .Where(x => !vehicleId.HasValue || x.VehicleId == vehicleId.Value);

        query = includeAllStatuses
            ? query.Where(x =>
                ((x.PlannedEndDate ?? x.PlannedDate ?? x.Date).Date > from
                    || ((x.PlannedEndDate ?? x.PlannedDate ?? x.Date).Date == from
                        && (!x.PlannedEndTime.HasValue || x.PlannedEndTime > TimeSpan.Zero))) &&
                (x.PlannedDate ?? x.Date) < toExclusive)
            : query.Where(x =>
                x.Status == MaintenanceStatus.InProgress ||
                (x.Status == MaintenanceStatus.Planned && ((x.PlannedEndDate ?? x.PlannedDate ?? x.Date).Date > from
                    || ((x.PlannedEndDate ?? x.PlannedDate ?? x.Date).Date == from
                        && (!x.PlannedEndTime.HasValue || x.PlannedEndTime > TimeSpan.Zero))) && (x.PlannedDate ?? x.Date) < toExclusive));

        var maintenances = await query
            .Include(x => x.Vehicle)
                .ThenInclude(x => x.Brand)
            .Include(x => x.Vehicle)
                .ThenInclude(x => x.Model)
            .Include(x => x.Vehicle)
                .ThenInclude(x => x.OrganizationClient)
            .OrderByDescending(x => x.Status == MaintenanceStatus.InProgress)
            .ThenBy(x => x.PlannedDate ?? x.Date)
            .ThenByDescending(x => x.DateTimeCreation)
            .Take(500)
            .ToListAsync();

        return maintenances
            .Select(x => OrganizationWorkspaceMaintenanceMapper.Map(
                x,
                x.PlannedDate ?? x.Date))
            .ToList();
    }
}
