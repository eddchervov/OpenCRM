using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Work;

/// <summary> Сервис запросов о ходе работ по обслуживанию. </summary>
internal sealed class MaintenanceWorkQueryService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptionService,
    IOrganizationPermissionService permissionService,
    ISystemClock systemClock) : IMaintenanceWorkQueryService
{
    /// <summary> Зависимость для получения сотрудника организации. </summary>
    private readonly MaintenanceWorkEmployeeAccess _employeeAccess = new(context);

    /// <summary> Возвращает прогресс работ по обслуживанию. </summary>
    public async Task<IReadOnlyList<MaintenanceWorkProgressDto>> GetProgressAsync(
        string userId, Guid organizationId, Guid maintenanceId)
    {
        await subscriptionService.EnsureWorkspaceCanReadAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceView);

        var maintenance = await context.Maintenance.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == maintenanceId
                && item.Vehicle.OrganizationId == organizationId && !item.IsDeleted)
            ?? throw new KeyNotFoundException("Обслуживание не найдено.");
        var operations = await context.MaintenanceWorkOperations.AsNoTracking()
            .Where(operation => operation.MaintenanceId == maintenanceId)
            .ToDictionaryAsync(operation => operation.Id);
        var operationIds = operations.Keys.ToArray();
        var intervals = await context.MaintenanceWorkIntervals.AsNoTracking()
            .Where(interval => operationIds.Contains(interval.OperationId))
            .ToListAsync();
        var intervalsByOperation = intervals.ToLookup(interval => interval.OperationId);
        var pauses = await context.MaintenanceWorkEvents.AsNoTracking()
            .Where(workEvent => operationIds.Contains(workEvent.OperationId)
                && workEvent.Status == MaintenanceWorkStatus.Paused)
            .OrderByDescending(workEvent => workEvent.CreatedAt)
            .ToListAsync();
        var lastPauseByOperation = pauses.GroupBy(workEvent => workEvent.OperationId)
            .ToDictionary(group => group.Key, group => group.First().Reason);
        var completionEvents = await context.MaintenanceWorkEvents.AsNoTracking()
            .Where(workEvent => operationIds.Contains(workEvent.OperationId)
                && workEvent.Status == MaintenanceWorkStatus.Completed)
            .OrderByDescending(workEvent => workEvent.ResultVersion)
            .Select(workEvent => new
            {
                workEvent.OperationId,
                workEvent.MemberId,
                workEvent.Reason,
                workEvent.ResultVersion
            })
            .ToListAsync();
        var latestCompletions = completionEvents
            .GroupBy(workEvent => workEvent.OperationId)
            .ToDictionary(group => group.Key, group => group.First());
        var completingMemberIds = latestCompletions.Values.Select(workEvent => workEvent.MemberId).Distinct().ToArray();
        var completingMembers = await context.OrganizationMembers.AsNoTracking()
            .Where(member => completingMemberIds.Contains(member.Id))
            .Select(member => new { member.Id, Name = member.User.UserName ?? member.User.Email })
            .ToDictionaryAsync(member => member.Id, member => member.Name);
        var adjustments = await context.MaintenanceWorkReports.AsNoTracking()
            .Where(report => report.MaintenanceId == maintenanceId && report.Kind == "time")
            .GroupBy(report => report.DetailId)
            .Select(group => new { DetailId = group.Key, Minutes = group.Sum(report => report.TimeAdjustmentMinutes ?? 0) })
            .ToDictionaryAsync(item => item.DetailId, item => item.Minutes);
        var now = systemClock.UtcNow;

        return maintenance.Details.Where(detail => !MaintenanceWorkMapping.IsMaterial(detail))
            .Select(detail =>
            {
                operations.TryGetValue(detail.DetailId, out var operation);
                lastPauseByOperation.TryGetValue(detail.DetailId, out var pauseReason);
                latestCompletions.TryGetValue(detail.DetailId, out var latestCompletion);
                var hasLatestCompletion = operation?.Status == MaintenanceWorkStatus.Completed
                    && latestCompletion?.ResultVersion == operation.Version;
                completingMembers.TryGetValue(
                    hasLatestCompletion ? latestCompletion!.MemberId : Guid.Empty, out var completedBy);
                var completionReason = hasLatestCompletion ? latestCompletion!.Reason : null;
                var assignedEmployeeId = detail.EmployeeId ?? maintenance.EmployeeId;
                var activeTime = intervalsByOperation[detail.DetailId].Aggregate(TimeSpan.Zero,
                    (total, interval) => total + ((interval.EndedAt ?? now) - interval.StartedAt));
                activeTime += TimeSpan.FromMinutes(adjustments.GetValueOrDefault(detail.DetailId));

                return new MaintenanceWorkProgressDto
                {
                    DetailId = detail.DetailId,
                    Name = detail.Name,
                    EmployeeName = detail.EmployeeName ?? maintenance.Performer,
                    HasAssignedEmployee = assignedEmployeeId.HasValue,
                    CompletedBy = string.IsNullOrWhiteSpace(completionReason) ? null : completedBy,
                    CompletionReason = completionReason,
                    Status = (int)(operation?.Status ?? MaintenanceWorkStatus.Assigned),
                    Version = operation?.Version ?? 0,
                    ActiveTime = activeTime,
                    PauseReason = operation?.Status == MaintenanceWorkStatus.Paused ? pauseReason : null
                };
            })
            .ToList();
    }

    /// <summary> Возвращает производственный отчёт по обслуживанию. </summary>
    public async Task<MaintenanceProductionReportDto> GetProductionReportAsync(
        string userId, Guid organizationId, Guid maintenanceId)
    {
        await subscriptionService.EnsureWorkspaceCanReadAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceView);
        var maintenance = await context.Maintenance.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == maintenanceId
                && item.Vehicle.OrganizationId == organizationId && !item.IsDeleted)
            ?? throw new KeyNotFoundException("Обслуживание не найдено.");
        var detailIds = maintenance.Details.Where(detail => !MaintenanceWorkMapping.IsMaterial(detail))
            .Select(detail => detail.DetailId).ToArray();
        var operations = await context.MaintenanceWorkOperations.AsNoTracking()
            .Where(operation => operation.MaintenanceId == maintenanceId)
            .ToListAsync();
        var intervals = await context.MaintenanceWorkIntervals.AsNoTracking()
            .Where(interval => detailIds.Contains(interval.OperationId))
            .ToListAsync();
        var adjustments = await context.MaintenanceWorkReports.AsNoTracking()
            .Where(report => report.MaintenanceId == maintenanceId && report.Kind == "time")
            .ToListAsync();
        var movements = await context.OrganizationMaterialMovements.AsNoTracking()
            .Include(movement => movement.Stock)
            .Where(movement => movement.OrganizationId == organizationId
                && movement.MaintenanceId == maintenanceId
                && movement.TypeCode == OrganizationMaterialMovementTypeCodes.MaintenanceIssue)
            .ToListAsync();
        var canViewFinance = await permissionService.HasPermissionAsync(
            userId, organizationId, OrganizationPermissionCodes.FinanceView);
        var now = systemClock.UtcNow;
        var activeTime = intervals.Aggregate(TimeSpan.Zero,
            (total, interval) => total + ((interval.EndedAt ?? now) - interval.StartedAt));
        activeTime += TimeSpan.FromMinutes(adjustments.Sum(report => report.TimeAdjustmentMinutes ?? 0));
        var materialCost = movements.Sum(movement => movement.Quantity < 0
            ? movement.TotalCost : -movement.TotalCost);
        var revenue = MaintenanceReadModelRules.GetNetAmount(maintenance);
        var materials = movements.GroupBy(movement => new { movement.StockId, movement.Stock.Name, movement.Unit })
            .Select(group => new MaintenanceProductionMaterialDto
            {
                Name = group.Key.Name,
                Unit = group.Key.Unit,
                Quantity = -group.Sum(movement => movement.Quantity),
                Cost = canViewFinance ? group.Sum(movement => movement.Quantity < 0
                    ? movement.TotalCost : -movement.TotalCost) : null
            })
            .Where(item => item.Quantity != 0)
            .OrderBy(item => item.Name)
            .ToList();

        return new MaintenanceProductionReportDto
        {
            MaintenanceId = maintenanceId,
            TotalOperations = detailIds.Length,
            CompletedOperations = operations.Count(operation => operation.Status == MaintenanceWorkStatus.Completed),
            ActiveTime = activeTime,
            TimeAdjustmentsCount = adjustments.Count,
            Materials = materials,
            MaterialCostAmount = canViewFinance ? materialCost : null,
            LaborCostAmount = canViewFinance ? maintenance.LaborCostAmount : null,
            RevenueAmount = canViewFinance ? revenue : null,
            GrossMarginAmount = canViewFinance ? revenue - materialCost - maintenance.LaborCostAmount : null
        };
    }

    /// <summary> Возвращает работы, назначенные сотруднику. </summary>
    public async Task<IReadOnlyList<MaintenanceWorkItemDto>> GetAssignedAsync(string userId, Guid organizationId)
    {
        await subscriptionService.EnsureWorkspaceCanReadAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkView);
        var employee = await this._employeeAccess.GetEmployeeAsync(userId, organizationId);
        var timeZoneId = await context.Organizations
            .AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.ShiftTimeZoneId)
            .SingleOrDefaultAsync();
        var timeZone = string.IsNullOrWhiteSpace(timeZoneId)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var today = TimeZoneInfo.ConvertTimeFromUtc(systemClock.UtcNow, timeZone).Date;
        var completedSince = DateTime.SpecifyKind(today.AddDays(-2), DateTimeKind.Utc);

        var maintenances = await context.Maintenance.AsNoTracking()
            .Include(maintenance => maintenance.Vehicle)
            .Where(maintenance => maintenance.Vehicle.OrganizationId == organizationId
                && !maintenance.IsDeleted
                && (maintenance.Status == MaintenanceStatus.Planned
                    || maintenance.Status == MaintenanceStatus.InProgress
                    || maintenance.Status == MaintenanceStatus.Completed && maintenance.CompletedDate >= completedSince))
            .OrderByDescending(maintenance => maintenance.Date)
            .ToListAsync();
        var maintenanceIds = maintenances.Select(maintenance => maintenance.Id).ToArray();
        var operations = await context.MaintenanceWorkOperations.AsNoTracking()
            .Where(operation => maintenanceIds.Contains(operation.MaintenanceId))
            .ToDictionaryAsync(operation => operation.Id);
        var operationIds = operations.Keys.ToArray();
        var intervals = await context.MaintenanceWorkIntervals.AsNoTracking()
            .Where(interval => operationIds.Contains(interval.OperationId))
            .ToListAsync();
        var intervalsByOperation = intervals.ToLookup(interval => interval.OperationId);
        var adjustments = await context.MaintenanceWorkReports.AsNoTracking()
            .Where(report => maintenanceIds.Contains(report.MaintenanceId) && report.Kind == "time")
            .GroupBy(report => report.DetailId)
            .Select(group => new { DetailId = group.Key, Minutes = group.Sum(report => report.TimeAdjustmentMinutes ?? 0) })
            .ToDictionaryAsync(item => item.DetailId, item => item.Minutes);
        var reworkReasons = (await context.MaintenanceWorkEvents.AsNoTracking()
            .Where(workEvent => maintenanceIds.Contains(workEvent.MaintenanceId)
                && workEvent.PreviousStatus == MaintenanceWorkStatus.Completed
                && workEvent.Status == MaintenanceWorkStatus.Assigned)
            .OrderByDescending(workEvent => workEvent.CreatedAt)
            .Select(workEvent => new { workEvent.OperationId, workEvent.Reason })
            .ToListAsync())
            .GroupBy(workEvent => workEvent.OperationId)
            .ToDictionary(group => group.Key, group => group.First().Reason);
        var result = new List<MaintenanceWorkItemDto>();

        foreach (var maintenance in maintenances)
        {
            foreach (var detail in maintenance.Details)
            {
                if (MaintenanceWorkMapping.IsMaterial(detail)
                    || !MaintenanceWorkMapping.IsAssignedTo(maintenance, detail, employee.Id))
                {
                    continue;
                }

                operations.TryGetValue(detail.DetailId, out var operation);
                if (maintenance.Status == MaintenanceStatus.Completed
                    && operation?.Status != MaintenanceWorkStatus.Completed)
                {
                    continue;
                }

                result.Add(MaintenanceWorkMapping.MapItem(
                    maintenance, detail, operation, intervalsByOperation[detail.DetailId], systemClock.UtcNow,
                    adjustmentMinutes: adjustments.GetValueOrDefault(detail.DetailId),
                    reworkReason: operation?.Status == MaintenanceWorkStatus.Completed
                        ? null : reworkReasons.GetValueOrDefault(detail.DetailId)));
            }
        }

        return result
            .OrderBy(item => item.Status is MaintenanceWorkStatus.InProgress or MaintenanceWorkStatus.Paused ? 0 : 1)
            .ThenBy(item => item.PlannedDate ?? DateTime.MaxValue)
            .ThenBy(item => item.PlannedStartTime ?? TimeSpan.MaxValue)
            .ThenBy(item => item.Name)
            .ToList();
    }

    /// <summary> Возвращает доступные для назначения работы. </summary>
    public async Task<IReadOnlyList<MaintenanceWorkItemDto>> GetAvailableAsync(string userId, Guid organizationId)
    {
        await subscriptionService.EnsureWorkspaceCanReadAsync(
            userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkView);
        _ = await this._employeeAccess.GetEmployeeAsync(userId, organizationId);
        var organization = await context.Organizations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == organizationId && !item.IsDeleted && item.IsActive)
            ?? throw new KeyNotFoundException("Организация не найдена.");
        if (!organization.AllowWorkSelfAssignment)
        {
            return [];
        }

        var maintenances = await context.Maintenance.AsNoTracking()
            .Include(maintenance => maintenance.Vehicle)
            .Where(maintenance => maintenance.Vehicle.OrganizationId == organizationId
                && !maintenance.IsDeleted && maintenance.EmployeeId == null
                && (maintenance.Status == MaintenanceStatus.Planned
                    || maintenance.Status == MaintenanceStatus.InProgress)
                && maintenance.ReadyForHandoverAt == null && maintenance.HandedOverAt == null)
            .ToListAsync();
        var maintenanceIds = maintenances.Select(maintenance => maintenance.Id).ToArray();
        var trackedDetailIds = await context.MaintenanceWorkOperations.AsNoTracking()
            .Where(operation => maintenanceIds.Contains(operation.MaintenanceId))
            .Select(operation => operation.Id)
            .ToListAsync();
        var trackedDetailIdSet = trackedDetailIds.ToHashSet();
        var result = new List<MaintenanceWorkItemDto>();
        foreach (var maintenance in maintenances)
        {
            foreach (var detail in maintenance.Details)
            {
                if (MaintenanceWorkMapping.IsMaterial(detail) || detail.EmployeeId.HasValue
                    || trackedDetailIdSet.Contains(detail.DetailId))
                {
                    continue;
                }

                result.Add(MaintenanceWorkMapping.MapItem(
                    maintenance, detail, null, [], systemClock.UtcNow));
            }
        }

        return result.OrderBy(item => item.PlannedDate ?? DateTime.MaxValue)
            .ThenBy(item => item.PlannedStartTime ?? TimeSpan.MaxValue)
            .ThenBy(item => item.Name)
            .ToList();
    }

    /// <summary> Возвращает отчёты по работам обслуживания с фильтром по строке. </summary>
    public async Task<IReadOnlyList<MaintenanceWorkReportDto>> GetReportsAsync(
        string userId, Guid organizationId, Guid maintenanceId, Guid? detailId = null)
    {
        if (detailId.HasValue)
        {
            await subscriptionService.EnsureWorkspaceCanReadAsync(
                userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkView);
            var employee = await this._employeeAccess.GetEmployeeAsync(userId, organizationId);
            var maintenance = await context.Maintenance.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == maintenanceId
                    && item.Vehicle.OrganizationId == organizationId && !item.IsDeleted)
                ?? throw new KeyNotFoundException("Обслуживание не найдено.");
            var detail = maintenance.Details.SingleOrDefault(item => item.DetailId == detailId.Value
                && !MaintenanceWorkMapping.IsMaterial(item));
            if (detail == null || !MaintenanceWorkMapping.IsAssignedTo(maintenance, detail, employee.Id))
            {
                throw new UnauthorizedAccessException("Работа не назначена этому исполнителю.");
            }
        }
        else
        {
            await subscriptionService.EnsureWorkspaceCanReadAsync(
                userId, organizationId, OrganizationPermissionCodes.MaintenanceView);
        }

        var reports = await context.MaintenanceWorkReports.AsNoTracking()
            .Where(report => report.OrganizationId == organizationId && report.MaintenanceId == maintenanceId
                && (!detailId.HasValue || report.DetailId == detailId.Value))
            .OrderByDescending(report => report.CreatedAt)
            .ToListAsync();
        var employeeIds = reports.Select(report => report.EmployeeId).Distinct().ToArray();
        var employeeNames = await context.OrganizationEmployees.AsNoTracking()
            .Where(employee => employeeIds.Contains(employee.Id))
            .ToDictionaryAsync(employee => employee.Id, employee => employee.Name);

        return reports.Select(report => MaintenanceWorkMapping.MapReport(
            report, employeeNames.GetValueOrDefault(report.EmployeeId) ?? "Исполнитель")).ToList();
    }
}
