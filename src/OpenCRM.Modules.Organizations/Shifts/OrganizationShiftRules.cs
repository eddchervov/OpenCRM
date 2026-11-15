using System.Globalization;
using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using DomainMaintenance = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Organizations.Shifts;

/// <summary> Правила работы со сменами сотрудников организации. </summary>
internal static class OrganizationShiftRules
{
    /// <summary> Обрезает сегменты назначения по границам суток. </summary>
    internal static IReadOnlyList<OrganizationShiftAssignmentSegmentDto> ClipSegmentsToDay(
        IReadOnlyList<OrganizationShiftAssignmentSegmentDto> segments,
        DateOnly day)
    {
        var dayStart = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00";
        var dayEnd = day.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00";
        var clipped = new List<OrganizationShiftAssignmentSegmentDto>();
        foreach (var segment in segments)
        {
            var start = string.CompareOrdinal(segment.StartLocal, dayStart) < 0 ? dayStart : segment.StartLocal;
            var end = string.CompareOrdinal(segment.EndLocal, dayEnd) > 0 ? dayEnd : segment.EndLocal;
            if (string.CompareOrdinal(start, end) >= 0)
            {
                continue;
            }

            clipped.Add(new OrganizationShiftAssignmentSegmentDto
            {
                StartLocal = start,
                EndLocal = end,
                IsOutsideShift = segment.IsOutsideShift
            });
        }

        return clipped;
    }

    /// <summary> Строит сегменты назначения сотрудника на смены для обслуживания. </summary>
    internal static IReadOnlyList<OrganizationShiftAssignmentSegmentDto> BuildAssignmentSegments(
        DomainMaintenance maintenance,
        Guid employeeId,
        TimeZoneInfo zone,
        IReadOnlyList<OrganizationEmployeeShift> shifts)
    {
        if (!maintenance.PlannedDate.HasValue || !maintenance.PlannedStartTime.HasValue
            || !maintenance.PlannedEndTime.HasValue)
        {
            return [];
        }

        var startLocal = DateTime.SpecifyKind(
            maintenance.PlannedDate.Value.Date.Add(maintenance.PlannedStartTime.Value), DateTimeKind.Unspecified);
        var endLocal = DateTime.SpecifyKind(
            (maintenance.PlannedEndDate ?? maintenance.PlannedDate.Value).Date.Add(maintenance.PlannedEndTime.Value),
            DateTimeKind.Unspecified);
        if (endLocal <= startLocal || zone.IsInvalidTime(startLocal) || zone.IsInvalidTime(endLocal)
            || zone.IsAmbiguousTime(startLocal) || zone.IsAmbiguousTime(endLocal))
        {
            return [];
        }

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, zone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, zone);
        var employeeShifts = shifts.Where(shift => shift.EmployeeId == employeeId
            && shift.IsPublished && !shift.IsCancelled
            && shift.PlanStartUtc < endUtc && shift.PlanEndUtc > startUtc).ToList();
        var boundaries = new List<DateTime> { startUtc, endUtc };
        foreach (var shift in employeeShifts)
        {
            boundaries.Add(startUtc > shift.PlanStartUtc ? startUtc : shift.PlanStartUtc);
            boundaries.Add(endUtc < shift.PlanEndUtc ? endUtc : shift.PlanEndUtc);
        }

        var orderedBoundaries = boundaries.Distinct().Order().ToList();
        var segments = new List<OrganizationShiftAssignmentSegmentDto>();
        for (var index = 0; index < orderedBoundaries.Count - 1; index++)
        {
            var segmentStart = orderedBoundaries[index];
            var segmentEnd = orderedBoundaries[index + 1];
            segments.Add(new OrganizationShiftAssignmentSegmentDto
            {
                StartLocal = TimeZoneInfo.ConvertTimeFromUtc(segmentStart, zone)
                    .ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                EndLocal = TimeZoneInfo.ConvertTimeFromUtc(segmentEnd, zone)
                    .ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                IsOutsideShift = !OrganizationShiftRules.IsCoveredByShifts(employeeShifts, segmentStart, segmentEnd)
            });
        }

        return segments;
    }

    /// <summary> Проверяет, покрывается ли интервал времени сменами. </summary>
    internal static bool IsCoveredByShifts(
        IReadOnlyList<OrganizationEmployeeShift> shifts,
        DateTime startUtc,
        DateTime endUtc)
    {
        var coveredUntil = startUtc;
        foreach (var shift in shifts.OrderBy(item => item.PlanStartUtc))
        {
            if (shift.PlanEndUtc <= coveredUntil)
            {
                continue;
            }

            if (shift.PlanStartUtc > coveredUntil)
            {
                return false;
            }

            coveredUntil = shift.PlanEndUtc;
            if (coveredUntil >= endUtc)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary> Преобразует смену в DTO с учётом часового пояса. </summary>
    internal static OrganizationShiftDto Map(OrganizationEmployeeShift shift, TimeZoneInfo zone)
    {
        var startLocal = TimeZoneInfo.ConvertTimeFromUtc(shift.PlanStartUtc, zone);
        var endLocal = TimeZoneInfo.ConvertTimeFromUtc(shift.PlanEndUtc, zone);

        return new OrganizationShiftDto
        {
            Id = shift.Id,
            EmployeeId = shift.EmployeeId,
            EmployeeName = shift.Employee.Name,
            LocalStartDate = DateOnly.FromDateTime(startLocal),
            TimeZoneId = zone.Id,
            StartLocal = startLocal.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
            EndLocal = endLocal.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
            PlanStartUtc = shift.PlanStartUtc,
            PlanEndUtc = shift.PlanEndUtc,
            PlannedBreakMinutes = shift.PlannedBreakMinutes,
            IsPublished = shift.IsPublished,
            IsCancelled = shift.IsCancelled,
            Version = shift.Version
        };
    }

    /// <summary> Разрешает часовой пояс по идентификатору. </summary>
    internal static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException("Часовой пояс не найден.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new InvalidOperationException("Часовой пояс недоступен.");
        }
    }

    /// <summary> Проверяет корректность локального времени для зоны. </summary>
    internal static DateTime ValidateLocalTime(DateTime value, TimeZoneInfo zone)
    {
        if (value.Kind != DateTimeKind.Unspecified || zone.IsInvalidTime(value) || zone.IsAmbiguousTime(value))
        {
            throw new InvalidOperationException("Укажите существующее и однозначное местное время смены.");
        }

        return value;
    }

    /// <summary> Проверяет, что дата является началом недели. </summary>
    internal static void ValidateWeekStart(DateOnly weekStart)
    {
        if (weekStart.DayOfWeek != DayOfWeek.Monday)
        {
            throw new InvalidOperationException("Неделя должна начинаться с понедельника.");
        }
    }
}
