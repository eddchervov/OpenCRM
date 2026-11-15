using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Shifts;

/// <summary> Сервис чтения смен сотрудников организации. </summary>
internal sealed class OrganizationShiftQueryService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptionService,
    IOrganizationPermissionService permissionService)
{
    /// <summary> Возвращает смены сотрудников организации за указанную неделю. </summary>
    internal async Task<OrganizationShiftWeekDto> GetWeekAsync(string userId, Guid organizationId, DateOnly weekStart)
    {
        await subscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.ShiftsView);
        OrganizationShiftRules.ValidateWeekStart(weekStart);

        var organization = await context.Organizations.AsNoTracking()
            .SingleAsync(item => item.Id == organizationId && item.IsActive && !item.IsDeleted);
        var weekEnd = weekStart.AddDays(7);
        var timeZone = string.IsNullOrWhiteSpace(organization.ShiftTimeZoneId)
            ? TimeZoneInfo.Utc
            : OrganizationShiftRules.ResolveTimeZone(organization.ShiftTimeZoneId);
        var weekStartUtc = TimeZoneInfo.ConvertTimeToUtc(weekStart.ToDateTime(TimeOnly.MinValue), timeZone);
        var weekEndUtc = TimeZoneInfo.ConvertTimeToUtc(weekEnd.ToDateTime(TimeOnly.MinValue), timeZone);
        var shifts = await context.OrganizationEmployeeShifts.AsNoTracking()
            .Include(item => item.Employee)
            .Where(item => item.OrganizationId == organizationId
                && item.PlanStartUtc < weekEndUtc && item.PlanEndUtc > weekStartUtc)
            .OrderBy(item => item.PlanStartUtc)
            .ToListAsync();
        var canViewAssignments = await permissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceView);
        var assignments = canViewAssignments
            ? await this.GetAssignmentsAsync(organizationId, weekStart, organization.ShiftTimeZoneId, shifts)
            : [];
        var scheduledEmployeeIds = shifts.Select(item => item.EmployeeId)
            .Concat(assignments.Select(item => item.EmployeeId)).Distinct().ToArray();
        var employees = await context.OrganizationEmployees.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && (item.IsActive || scheduledEmployeeIds.Contains(item.Id)))
            .OrderBy(item => item.Name)
            .Select(item => new OrganizationEmployeeOptionDto { Id = item.Id, Name = item.Name, IsActive = item.IsActive })
            .ToListAsync();

        return new OrganizationShiftWeekDto
        {
            TimeZoneId = organization.ShiftTimeZoneId,
            CanManage = await permissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.ShiftsManage),
            CanViewAssignments = canViewAssignments,
            Employees = employees,
            Shifts = shifts.Select(shift => OrganizationShiftRules.Map(shift, timeZone)).ToList(),
            Assignments = assignments
        };
    }

    /// <summary> Возвращает смены текущего сотрудника в организации. </summary>
    internal async Task<IReadOnlyList<OrganizationShiftDto>> GetOwnAsync(string userId, Guid organizationId)
    {
        await subscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.ShiftsSelfView);

        var employeeId = await context.OrganizationEmployees.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.IsActive
                && item.Member != null && item.Member.UserId == userId)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync();
        if (!employeeId.HasValue)
        {
            return [];
        }

        var earliest = DateTime.UtcNow;
        var latest = DateTime.UtcNow.AddDays(14);
        var shifts = await context.OrganizationEmployeeShifts.AsNoTracking()
            .Include(item => item.Employee)
            .Where(item => item.OrganizationId == organizationId && item.EmployeeId == employeeId
                && item.IsPublished && !item.IsCancelled
                && item.PlanEndUtc >= earliest && item.PlanStartUtc < latest)
            .OrderBy(item => item.PlanStartUtc)
            .ToListAsync();

        var organizationTimeZoneId = await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organizationId)
            .Select(item => item.ShiftTimeZoneId)
            .SingleAsync();
        var timeZone = string.IsNullOrWhiteSpace(organizationTimeZoneId)
            ? TimeZoneInfo.Utc
            : OrganizationShiftRules.ResolveTimeZone(organizationTimeZoneId);

        return shifts.Select(shift => OrganizationShiftRules.Map(shift, timeZone)).ToList();
    }

    /// <summary> Возвращает доступность сотрудника в заданном локальном интервале. </summary>
    internal async Task<OrganizationShiftAvailabilityDto> GetAvailabilityAsync(
        string userId,
        Guid organizationId,
        Guid employeeId,
        string startLocal,
        string endLocal)
    {
        await subscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.ShiftsView);
        if (!DateTime.TryParseExact(startLocal, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start)
            || !DateTime.TryParseExact(endLocal, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var end)
            || end <= start || end - start > TimeSpan.FromDays(31))
        {
            throw new InvalidOperationException("Укажите корректное время обслуживания.");
        }

        var employeeExists = await context.OrganizationEmployees.AsNoTracking()
            .AnyAsync(item => item.Id == employeeId && item.OrganizationId == organizationId);
        if (!employeeExists)
        {
            throw new KeyNotFoundException("Сотрудник не найден.");
        }

        var timeZoneId = await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organizationId && item.IsActive && !item.IsDeleted)
            .Select(item => item.ShiftTimeZoneId)
            .SingleAsync();
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return new OrganizationShiftAvailabilityDto();
        }

        var zone = OrganizationShiftRules.ResolveTimeZone(timeZoneId);
        if (zone.IsInvalidTime(start) || zone.IsInvalidTime(end)
            || zone.IsAmbiguousTime(start) || zone.IsAmbiguousTime(end))
        {
            return new OrganizationShiftAvailabilityDto();
        }

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(start, zone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(end, zone);
        var shifts = await context.OrganizationEmployeeShifts.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.EmployeeId == employeeId
                && item.IsPublished && !item.IsCancelled
                && item.PlanStartUtc < endUtc && item.PlanEndUtc > startUtc)
            .OrderBy(item => item.PlanStartUtc)
            .ToListAsync();

        return new OrganizationShiftAvailabilityDto
        {
            IsAvailable = OrganizationShiftRules.IsCoveredByShifts(shifts, startUtc, endUtc)
        };
    }

    /// <summary> Загружает назначения сотрудников на смены недели. </summary>
    private async Task<IReadOnlyList<OrganizationShiftAssignmentDto>> GetAssignmentsAsync(
        Guid organizationId,
        DateOnly weekStart,
        string? timeZoneId,
        IReadOnlyList<OrganizationEmployeeShift> shifts)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return [];
        }

        var zone = OrganizationShiftRules.ResolveTimeZone(timeZoneId);
        var weekEnd = weekStart.AddDays(7);
        var from = DateTime.SpecifyKind(weekStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var to = from.AddDays(7);
        var assignmentRows = await context.Database.SqlQuery<MaintenanceEmployeeAssignmentRow>(
            $"""
            SELECT "MaintenanceId", "DetailId", "EmployeeId"
            FROM "MaintenanceEmployeeAssignments"
            WHERE "OrganizationId" = {organizationId}
                AND NOT "IsDeleted"
                AND "Status" NOT IN (3, 4)
                AND "PlannedDate" < {to}
                AND COALESCE("PlannedEndDate", "PlannedDate") >= {from}
            """).ToListAsync();

        var maintenanceIds = assignmentRows.Select(row => row.MaintenanceId).Distinct().ToArray();
        var maintenances = await context.Maintenance.AsNoTracking()
            .Include(item => item.Vehicle)
            .Where(item => maintenanceIds.Contains(item.Id) && item.Vehicle.OrganizationId == organizationId
                && !item.IsDeleted && item.Status != MaintenanceStatus.Completed
                && item.Status != MaintenanceStatus.Cancelled)
            .ToListAsync();
        var rowsByMaintenance = assignmentRows.GroupBy(row => row.MaintenanceId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var assignments = new List<OrganizationShiftAssignmentDto>();

        foreach (var maintenance in maintenances)
        {
            if (!maintenance.PlannedDate.HasValue || !rowsByMaintenance.TryGetValue(maintenance.Id, out var rows))
            {
                continue;
            }

            foreach (var employeeRows in rows.GroupBy(row => row.EmployeeId))
            {
                var employeeId = employeeRows.Key;
                var serviceCount = employeeRows.Count(row => row.DetailId != Guid.Empty);
                var segments = OrganizationShiftRules.BuildAssignmentSegments(maintenance, employeeId, zone, shifts);
                var firstDay = DateOnly.FromDateTime(maintenance.PlannedDate.Value);
                var lastDay = DateOnly.FromDateTime(maintenance.PlannedEndDate ?? maintenance.PlannedDate.Value);
                for (var day = firstDay < weekStart ? weekStart : firstDay;
                    day < weekEnd && day <= lastDay;
                    day = day.AddDays(1))
                {
                    var daySegments = OrganizationShiftRules.ClipSegmentsToDay(segments, day);
                    if (segments.Count > 0 && daySegments.Count == 0)
                    {
                        continue;
                    }

                    assignments.Add(new OrganizationShiftAssignmentDto
                    {
                        MaintenanceId = maintenance.Id,
                        EmployeeId = employeeId,
                        VehicleName = maintenance.Vehicle.RegistrationPlate ?? maintenance.Vehicle.Name ?? "Обслуживание",
                        LocalStartDate = day,
                        ServiceCount = serviceCount,
                        Segments = daySegments
                    });
                }
            }
        }

        return assignments;
    }
}
