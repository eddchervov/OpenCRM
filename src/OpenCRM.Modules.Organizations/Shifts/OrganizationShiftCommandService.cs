using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Shifts;

/// <summary> Сервис команд управления сменами организации. </summary>
internal sealed class OrganizationShiftCommandService(
    OpenCRMContext context,
    IOrganizationSubscriptionService subscriptionService,
    IOrganizationAuditService auditService,
    OrganizationTransaction transaction)
{
    /// <summary> Устанавливает часовой пояс организации. </summary>
    internal async Task SetTimeZoneAsync(string userId, Guid organizationId, string timeZoneId, string? expectedTimeZoneId = null)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(
            userId,
            organizationId,
            OrganizationPermissionCodes.WorkspaceProfileEdit);
        var normalizedTimeZoneId = timeZoneId?.Trim() ?? string.Empty;
        if (normalizedTimeZoneId.Length is < 1 or > 100 || !normalizedTimeZoneId.Contains('/'))
        {
            throw new InvalidOperationException("Выберите часовой пояс IANA.");
        }

        OrganizationShiftRules.ResolveTimeZone(normalizedTimeZoneId);

        await transaction.ExecuteAuditedAsync(async eventId =>
        {
            await transaction.LockOrganizationAsync(organizationId);
            var organization = await context.Organizations.SingleAsync(item =>
                item.Id == organizationId && item.IsActive && !item.IsDeleted);
            if (!OrganizationShiftCommandService.AreEquivalentTimeZoneIds(
                    organization.ShiftTimeZoneId,
                    normalizedTimeZoneId)
                && await OrganizationShiftCommandService.HasTimedSchedulingDataAsync(context, organizationId))
            {
                throw new InvalidOperationException("Часовой пояс нельзя изменить после создания расписания с заданным временем.");
            }

            if (!OrganizationShiftCommandService.AreEquivalentTimeZoneIds(organization.ShiftTimeZoneId, normalizedTimeZoneId)
                && !string.Equals(organization.ShiftTimeZoneId, expectedTimeZoneId, StringComparison.Ordinal))
            {
                throw new DbUpdateConcurrencyException("Часовой пояс уже изменён. Обновите настройки и повторите попытку.");
            }

            organization.ShiftTimeZoneId = normalizedTimeZoneId;
            organization.UpdatedAt = DateTime.UtcNow;
            await auditService.AddAsync(new OrganizationAuditEventCreateDto
            {
                EventId = eventId,
                OrganizationId = organizationId,
                ActorUserId = userId,
                EventType = "shifts.timezone.changed",
                Title = "Часовой пояс смен изменён",
                Description = normalizedTimeZoneId,
                TargetType = "organization",
                TargetId = organizationId.ToString("D")
            }, saveChanges: false);
            await context.SaveChangesAsync();
        });
    }

    /// <summary> Сохраняет смену или создаёт новую. </summary>
    internal async Task<Guid> SaveAsync(
        string userId,
        Guid organizationId,
        Guid? shiftId,
        SaveOrganizationShiftDto dto)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ShiftsManage);
        var id = shiftId ?? Guid.NewGuid();
        var now = DateTime.UtcNow;
        var isRetry = false;

        await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(async () =>
        {
            if (isRetry)
            {
                context.ChangeTracker.Clear();
            }

            isRetry = true;
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Organizations\" WHERE \"Id\" = {organizationId} FOR NO KEY UPDATE");
            var organization = await context.Organizations.SingleAsync(item =>
                item.Id == organizationId && item.IsActive && !item.IsDeleted);
            var employee = await context.OrganizationEmployees.SingleOrDefaultAsync(item =>
                item.Id == dto.EmployeeId && item.OrganizationId == organizationId);
            if (employee == null || !employee.IsActive)
            {
                throw new InvalidOperationException("Выберите действующего исполнителя этой организации.");
            }

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "OrganizationEmployees" WHERE "Id" = {employee.Id} FOR UPDATE""");

            OrganizationEmployeeShift shift;
            if (shiftId.HasValue)
            {
                shift = await context.OrganizationEmployeeShifts.SingleOrDefaultAsync(item =>
                    item.Id == shiftId && item.OrganizationId == organizationId)
                    ?? throw new KeyNotFoundException("Смена не найдена.");
                if (shift.IsCancelled || !dto.Version.HasValue || shift.Version != dto.Version.Value)
                {
                    throw new DbUpdateConcurrencyException("Смена уже изменена. Обновите график.");
                }

                if (shift.IsPublished && string.IsNullOrWhiteSpace(dto.Reason))
                {
                    throw new InvalidOperationException("Укажите причину изменения опубликованной смены.");
                }
            }
            else
            {
                shift = new OrganizationEmployeeShift
                {
                    Id = id,
                    OrganizationId = organizationId,
                    EmployeeId = employee.Id,
                    CreatedAt = now
                };
            }

            var timeZoneId = organization.ShiftTimeZoneId;
            if (string.IsNullOrWhiteSpace(timeZoneId))
            {
                throw new InvalidOperationException("Сначала выберите часовой пояс графика.");
            }

            var zone = OrganizationShiftRules.ResolveTimeZone(timeZoneId);
            var startLocal = OrganizationShiftRules.ValidateLocalTime(dto.StartLocal, zone);
            var endLocal = OrganizationShiftRules.ValidateLocalTime(dto.EndLocal, zone);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, zone);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, zone);
            var duration = endUtc - startUtc;
            if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(24)
                || dto.PlannedBreakMinutes < 0 || dto.PlannedBreakMinutes >= duration.TotalMinutes)
            {
                throw new InvalidOperationException("Проверьте начало, конец и длительность перерыва.");
            }

            var overlaps = await context.OrganizationEmployeeShifts.AnyAsync(item =>
                item.OrganizationId == organizationId && item.EmployeeId == employee.Id
                && item.Id != id && !item.IsCancelled
                && item.PlanStartUtc < endUtc && item.PlanEndUtc > startUtc);
            if (overlaps)
            {
                throw new InvalidOperationException("У исполнителя уже есть смена в это время.");
            }

            if (!shiftId.HasValue)
            {
                context.OrganizationEmployeeShifts.Add(shift);
            }

            shift.EmployeeId = employee.Id;
            shift.IsPublished = true;
            shift.LocalStartDate = DateOnly.FromDateTime(startLocal);
            shift.PlanStartUtc = startUtc;
            shift.PlanEndUtc = endUtc;
            shift.PlannedBreakMinutes = dto.PlannedBreakMinutes;
            shift.TimeZoneId = timeZoneId;
            shift.UpdatedAt = now;
            organization.UpdatedAt = now;
            await auditService.AddAsync(new OrganizationAuditEventCreateDto
            {
                OrganizationId = organizationId,
                ActorUserId = userId,
                EventType = shiftId.HasValue ? "shifts.updated" : "shifts.created",
                Title = shiftId.HasValue ? "Смена изменена" : "Смена добавлена и опубликована",
                Description = $"{employee.Name}: {startLocal:g}–{endLocal:g}. {dto.Reason?.Trim()}",
                TargetType = "shift",
                TargetId = id.ToString("D")
            }, saveChanges: false);
            await context.SaveChangesAsync();
        }, () => context.OrganizationEmployeeShifts.AsNoTracking()
            .AnyAsync(item => item.Id == id && item.UpdatedAt == now));

        return id;
    }

    /// <summary> Отменяет смену сотрудника с указанием причины. </summary>
    internal async Task CancelAsync(string userId, Guid organizationId, Guid shiftId, uint version, string? reason)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ShiftsManage);
        var shift = await context.OrganizationEmployeeShifts.SingleOrDefaultAsync(item =>
            item.Id == shiftId && item.OrganizationId == organizationId)
            ?? throw new KeyNotFoundException("Смена не найдена.");
        if (shift.IsCancelled || shift.Version != version)
        {
            throw new DbUpdateConcurrencyException("Смена уже изменена. Обновите график.");
        }

        if (shift.IsPublished && string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Укажите причину отмены опубликованной смены.");
        }

        shift.IsCancelled = true;
        shift.UpdatedAt = DateTime.UtcNow;
        await auditService.AddAsync(new OrganizationAuditEventCreateDto
        {
            OrganizationId = organizationId,
            ActorUserId = userId,
            EventType = "shifts.cancelled",
            Title = "Смена отменена",
            Description = reason?.Trim(),
            TargetType = "shift",
            TargetId = shiftId.ToString("D")
        }, saveChanges: false);
        await context.SaveChangesAsync();
    }

    /// <summary> Публикует смены на неделю. </summary>
    internal async Task<int> PublishWeekAsync(string userId, Guid organizationId, DateOnly weekStart)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ShiftsManage);
        OrganizationShiftRules.ValidateWeekStart(weekStart);

        var organizationTimeZoneId = await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organizationId && item.IsActive && !item.IsDeleted)
            .Select(item => item.ShiftTimeZoneId)
            .SingleAsync();
        if (string.IsNullOrWhiteSpace(organizationTimeZoneId))
        {
            throw new InvalidOperationException("Сначала выберите часовой пояс графика.");
        }

        var timeZone = OrganizationShiftRules.ResolveTimeZone(organizationTimeZoneId);
        var weekStartUtc = TimeZoneInfo.ConvertTimeToUtc(weekStart.ToDateTime(TimeOnly.MinValue), timeZone);
        var weekEndUtc = TimeZoneInfo.ConvertTimeToUtc(weekStart.AddDays(7).ToDateTime(TimeOnly.MinValue), timeZone);
        var shifts = await context.OrganizationEmployeeShifts
            .Where(item => item.OrganizationId == organizationId
                && item.PlanStartUtc >= weekStartUtc && item.PlanStartUtc < weekEndUtc
                && !item.IsPublished && !item.IsCancelled)
            .ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var shift in shifts)
        {
            shift.IsPublished = true;
            shift.UpdatedAt = now;
            await auditService.AddAsync(new OrganizationAuditEventCreateDto
            {
                OrganizationId = organizationId,
                ActorUserId = userId,
                EventType = "shifts.published",
                Title = "Смена опубликована",
                TargetType = "shift",
                TargetId = shift.Id.ToString("D")
            }, saveChanges: false);
        }

        await context.SaveChangesAsync();

        return shifts.Count;
    }

    /// <summary> Копирует смены сотрудников из предыдущей недели на новую неделю. </summary>
    internal async Task<int> CopyPreviousWeekAsync(string userId, Guid organizationId, DateOnly weekStart)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ShiftsManage);
        OrganizationShiftRules.ValidateWeekStart(weekStart);

        var now = DateTime.UtcNow;
        var copyIds = new Dictionary<Guid, Guid>();
        var copiedCount = 0;
        await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(async () =>
        {
            if (copyIds.Count > 0)
            {
                context.ChangeTracker.Clear();
            }

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Organizations\" WHERE \"Id\" = {organizationId} FOR NO KEY UPDATE");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""SELECT 1 FROM "OrganizationEmployees" WHERE "OrganizationId" = {organizationId} ORDER BY "Id" FOR UPDATE""");

            var organization = await context.Organizations.SingleAsync(item =>
                item.Id == organizationId && item.IsActive && !item.IsDeleted);
            if (string.IsNullOrWhiteSpace(organization.ShiftTimeZoneId))
            {
                throw new InvalidOperationException("Сначала выберите часовой пояс графика.");
            }

            var timeZone = OrganizationShiftRules.ResolveTimeZone(organization.ShiftTimeZoneId);
            var sourceWeekStartUtc = TimeZoneInfo.ConvertTimeToUtc(
                weekStart.AddDays(-7).ToDateTime(TimeOnly.MinValue), timeZone);
            var targetWeekStartUtc = TimeZoneInfo.ConvertTimeToUtc(
                weekStart.ToDateTime(TimeOnly.MinValue), timeZone);
            var previousWeekShifts = await context.OrganizationEmployeeShifts.AsNoTracking()
                .Include(item => item.Employee)
                .Where(item => item.OrganizationId == organizationId
                    && item.PlanStartUtc >= sourceWeekStartUtc && item.PlanStartUtc < targetWeekStartUtc
                    && !item.IsCancelled && item.Employee.IsActive)
                .OrderBy(item => item.PlanStartUtc)
                .ToListAsync();
            var copies = new List<ShiftCopyCandidate>(previousWeekShifts.Count);

            foreach (var source in previousWeekShifts)
            {
                var startLocal = DateTime.SpecifyKind(
                    TimeZoneInfo.ConvertTimeFromUtc(source.PlanStartUtc, timeZone).AddDays(7),
                    DateTimeKind.Unspecified);
                var endLocal = DateTime.SpecifyKind(
                    TimeZoneInfo.ConvertTimeFromUtc(source.PlanEndUtc, timeZone).AddDays(7),
                    DateTimeKind.Unspecified);
                OrganizationShiftRules.ValidateLocalTime(startLocal, timeZone);
                OrganizationShiftRules.ValidateLocalTime(endLocal, timeZone);

                var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, timeZone);
                var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, timeZone);
                var duration = endUtc - startUtc;
                if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(24)
                    || source.PlannedBreakMinutes < 0 || source.PlannedBreakMinutes >= duration.TotalMinutes)
                {
                    throw new InvalidOperationException("Не удалось повторить смену: проверьте её длительность и перерыв.");
                }

                copies.Add(new ShiftCopyCandidate(source, startLocal, endLocal, startUtc, endUtc));
            }

            if (copies.Count == 0)
            {
                copiedCount = 0;
                return;
            }

            var employeeIds = copies.Select(item => item.Source.EmployeeId).Distinct().ToArray();
            var earliestStart = copies.Min(item => item.StartUtc);
            var latestEnd = copies.Max(item => item.EndUtc);
            var existingShifts = await context.OrganizationEmployeeShifts.AsNoTracking()
                .Where(item => item.OrganizationId == organizationId && employeeIds.Contains(item.EmployeeId)
                    && !item.IsCancelled && item.PlanStartUtc < latestEnd && item.PlanEndUtc > earliestStart)
                .ToListAsync();
            foreach (var employeeCopies in copies.GroupBy(item => item.Source.EmployeeId))
            {
                var orderedCopies = employeeCopies.OrderBy(item => item.StartUtc).ToArray();
                var lastEnd = DateTime.MinValue;
                for (var index = 0; index < orderedCopies.Length; index++)
                {
                    var copy = orderedCopies[index];
                    if (existingShifts.Any(item => item.EmployeeId == copy.Source.EmployeeId
                            && item.PlanStartUtc < copy.EndUtc && item.PlanEndUtc > copy.StartUtc)
                        || lastEnd > copy.StartUtc)
                    {
                        throw new InvalidOperationException(
                            "Не удалось повторить неделю: у исполнителя уже есть смена в это время. Существующие смены не изменены.");
                    }

                    if (copy.EndUtc > lastEnd)
                    {
                        lastEnd = copy.EndUtc;
                    }
                }
            }

            foreach (var copy in copies)
            {
                if (!copyIds.TryGetValue(copy.Source.Id, out var copyId))
                {
                    copyId = Guid.NewGuid();
                    copyIds.Add(copy.Source.Id, copyId);
                }

                context.OrganizationEmployeeShifts.Add(new OrganizationEmployeeShift
                {
                    Id = copyId,
                    OrganizationId = organizationId,
                    EmployeeId = copy.Source.EmployeeId,
                    LocalStartDate = DateOnly.FromDateTime(copy.StartLocal),
                    PlanStartUtc = copy.StartUtc,
                    PlanEndUtc = copy.EndUtc,
                    PlannedBreakMinutes = copy.Source.PlannedBreakMinutes,
                    TimeZoneId = organization.ShiftTimeZoneId,
                    IsPublished = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                await auditService.AddAsync(new OrganizationAuditEventCreateDto
                {
                    OrganizationId = organizationId,
                    ActorUserId = userId,
                    EventType = "shifts.copied",
                    Title = "Смена скопирована из прошлой недели",
                    Description = $"{copy.Source.Employee.Name}: {copy.StartLocal:g}–{copy.EndLocal:g}",
                    TargetType = "shift",
                    TargetId = copyId.ToString("D")
                }, saveChanges: false);
            }

            organization.UpdatedAt = now;
            copiedCount = copies.Count;
            await context.SaveChangesAsync();
        }, async () =>
        {
            if (copyIds.Count == 0)
            {
                return true;
            }

            var copiedIds = copyIds.Values.ToArray();
            var persistedCount = await context.OrganizationEmployeeShifts.AsNoTracking()
                .CountAsync(item => copiedIds.Contains(item.Id) && item.UpdatedAt == now);

            return persistedCount == copiedIds.Length;
        });

        return copiedCount;
    }

    /// <summary> Кандидат на копирование смены с локальным и UTC-временем начала и конца. </summary>
    private sealed record ShiftCopyCandidate(
        OrganizationEmployeeShift Source,
        DateTime StartLocal,
        DateTime EndLocal,
        DateTime StartUtc,
        DateTime EndUtc);

    private static bool AreEquivalentTimeZoneIds(string? currentTimeZoneId, string normalizedTimeZoneId)
    {
        if (string.IsNullOrWhiteSpace(currentTimeZoneId))
        {
            return false;
        }

        var normalizedCurrentTimeZoneId = string.Equals(currentTimeZoneId, "UTC", StringComparison.OrdinalIgnoreCase)
            ? "Etc/UTC"
            : currentTimeZoneId;

        return string.Equals(normalizedCurrentTimeZoneId, normalizedTimeZoneId, StringComparison.Ordinal);
    }

    internal static async Task<bool> HasTimedSchedulingDataAsync(OpenCRMContext context, Guid organizationId) =>
        await context.Maintenance.IgnoreQueryFilters().AnyAsync(item =>
            item.Vehicle.OrganizationId == organizationId
            && (item.PlannedStartTime.HasValue || item.PlannedEndTime.HasValue))
        || await context.OrganizationEmployeeShifts.IgnoreQueryFilters()
            .AnyAsync(item => item.OrganizationId == organizationId);
}
