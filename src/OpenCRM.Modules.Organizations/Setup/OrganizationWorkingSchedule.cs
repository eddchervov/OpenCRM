using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Contracts.Dtos;

namespace OpenCRM.Modules.Organizations.Setup;

/// <summary> Работа с графиком работы организации. </summary>
internal sealed class OrganizationWorkingSchedule(OpenCRMContext context)
{
    /// <summary> Сохраняет график работы организации по данным настройки. </summary>
    internal async Task SaveAsync(Organization organization, SaveOrganizationSetupDto dto)
    {
        var existingDays = await context.OrganizationWorkingDays
            .Include(day => day.Breaks)
            .Where(day => day.OrganizationId == organization.Id)
            .ToListAsync();
        var schedule = dto.WeeklySchedule;
        if (schedule == null)
        {
            if (OrganizationWorkingSchedule.HasDetailed(existingDays))
            {
                throw new InvalidOperationException(
                    "Расписание содержит отдельные часы или перерывы. Обновите страницу и сохраните расписание целиком.");
            }

            schedule = OrganizationWorkingSchedule.CreateLegacy(dto);
        }

        var workingDays = OrganizationWorkingSchedule.Validate(schedule);
        this.Reconcile(organization.Id, existingDays, workingDays);
        organization.WorkingDays = workingDays.Select(day => day.DayOfWeek).ToArray();
        organization.WorkdayStart = workingDays.Min(day => day.StartTime);
        organization.WorkdayEnd = workingDays.Max(day => day.EndTime);
    }

    /// <summary> Определяет, задан ли подробный график по дням. </summary>
    internal static bool HasDetailed(IEnumerable<OrganizationWorkingDay> workingDays)
    {
        var days = workingDays.ToList();
        return days.Any(day => day.Breaks.Count > 0)
            || days.Select(day => (day.StartTime, day.EndTime)).Distinct().Skip(1).Any();
    }

    /// <summary> Создаёт дни графика из устаревших полей организации. </summary>
    internal static List<OrganizationWorkingDayDto> CreateLegacy(Organization organization)
    {
        return Enumerable.Range(0, 7)
            .Select(dayOfWeek => new OrganizationWorkingDayDto
            {
                DayOfWeek = dayOfWeek,
                IsWorking = organization.WorkingDays.Contains(dayOfWeek),
                StartTime = organization.WorkingDays.Contains(dayOfWeek) ? organization.WorkdayStart : null,
                EndTime = organization.WorkingDays.Contains(dayOfWeek) ? organization.WorkdayEnd : null
            })
            .ToList();
    }

    /// <summary> Создаёт дни графика из устаревших полей данных настройки. </summary>
    internal static List<OrganizationWorkingDayDto> CreateLegacy(SaveOrganizationSetupDto dto)
    {
        if (!dto.WorkdayStart.HasValue || !dto.WorkdayEnd.HasValue
            || dto.WorkdayEnd.Value <= dto.WorkdayStart.Value
            || dto.WorkingDays == null || dto.WorkingDays.Length == 0
            || dto.WorkingDays.Any(day => day is < 0 or > 6))
        {
            throw new InvalidOperationException("Выберите рабочие дни и время окончания позже начала.");
        }

        var selectedDays = dto.WorkingDays.ToHashSet();
        return Enumerable.Range(0, 7)
            .Select(dayOfWeek => new OrganizationWorkingDayDto
            {
                DayOfWeek = dayOfWeek,
                IsWorking = selectedDays.Contains(dayOfWeek),
                StartTime = selectedDays.Contains(dayOfWeek) ? dto.WorkdayStart : null,
                EndTime = selectedDays.Contains(dayOfWeek) ? dto.WorkdayEnd : null
            })
            .ToList();
    }

    /// <summary> Преобразует дни графика в DTO. </summary>
    internal static List<OrganizationWorkingDayDto> Create(IEnumerable<OrganizationWorkingDay> workingDays)
    {
        var daysByNumber = workingDays.ToDictionary(day => day.DayOfWeek);
        return Enumerable.Range(0, 7)
            .Select(dayOfWeek => daysByNumber.TryGetValue(dayOfWeek, out var workingDay)
                ? new OrganizationWorkingDayDto
                {
                    DayOfWeek = dayOfWeek,
                    IsWorking = true,
                    StartTime = workingDay.StartTime,
                    EndTime = workingDay.EndTime,
                    Breaks = workingDay.Breaks
                        .OrderBy(value => value.StartTime)
                        .Select(value => new OrganizationWorkingDayBreakDto
                        {
                            StartTime = value.StartTime,
                            EndTime = value.EndTime
                        })
                        .ToList()
                }
                : new OrganizationWorkingDayDto { DayOfWeek = dayOfWeek })
            .ToList();
    }

    /// <summary> Проверяет и нормализует список дней графика. </summary>
    internal static List<OrganizationWorkingDayDto> Validate(List<OrganizationWorkingDayDto> schedule)
    {
        if (schedule == null || schedule.Count != 7 || schedule.Any(day => day == null)
            || schedule.Select(day => day.DayOfWeek).Distinct().Count() != 7
            || schedule.Any(day => day.DayOfWeek is < 0 or > 6))
        {
            throw new InvalidOperationException("Укажите расписание для каждого дня недели.");
        }

        var workingDays = schedule.Where(day => day.IsWorking).OrderBy(day => day.DayOfWeek).ToList();
        if (workingDays.Count == 0)
        {
            throw new InvalidOperationException("Выберите хотя бы один рабочий день.");
        }

        foreach (var day in schedule)
        {
            if (day.Breaks == null || day.Breaks.Any(value => value is null))
            {
                throw new InvalidOperationException("Проверьте список перерывов.");
            }

            if (!day.IsWorking && (day.StartTime.HasValue || day.EndTime.HasValue || day.Breaks.Count > 0))
            {
                throw new InvalidOperationException("У закрытого дня не должно быть времени работы и перерывов.");
            }

            if (!day.IsWorking)
            {
                continue;
            }

            if (!day.StartTime.HasValue || !day.EndTime.HasValue || day.EndTime.Value <= day.StartTime.Value)
            {
                throw new InvalidOperationException("Время окончания рабочего дня должно быть позже времени начала.");
            }

            var breaks = day.Breaks.OrderBy(value => value.StartTime).ToList();
            for (var index = 0; index < breaks.Count; index++)
            {
                var currentBreak = breaks[index];
                if (currentBreak.StartTime >= currentBreak.EndTime
                    || currentBreak.StartTime < day.StartTime.Value || currentBreak.EndTime > day.EndTime.Value
                    || (index > 0 && breaks[index - 1].EndTime > currentBreak.StartTime))
                {
                    throw new InvalidOperationException(
                        "Проверьте время перерывов: они должны быть внутри рабочего дня и не пересекаться.");
                }
            }

            day.Breaks = breaks;
        }

        return workingDays;
    }

    /// <summary> Согласует дни графика с запрошенными значениями. </summary>
    private void Reconcile(
        Guid organizationId,
        List<OrganizationWorkingDay> existingDays,
        List<OrganizationWorkingDayDto> requestedDays)
    {
        var existingByDay = existingDays.ToDictionary(day => day.DayOfWeek);
        var requestedByDay = requestedDays.ToDictionary(day => day.DayOfWeek);
        foreach (var existingDay in existingDays.Where(day => !requestedByDay.ContainsKey(day.DayOfWeek)))
        {
            context.OrganizationWorkingDays.Remove(existingDay);
        }

        foreach (var requestedDay in requestedDays)
        {
            if (!existingByDay.TryGetValue(requestedDay.DayOfWeek, out var workingDay))
            {
                workingDay = new OrganizationWorkingDay
                {
                    OrganizationId = organizationId,
                    DayOfWeek = requestedDay.DayOfWeek,
                    StartTime = requestedDay.StartTime!.Value,
                    EndTime = requestedDay.EndTime!.Value
                };
                context.OrganizationWorkingDays.Add(workingDay);
            }
            else
            {
                workingDay.StartTime = requestedDay.StartTime!.Value;
                workingDay.EndTime = requestedDay.EndTime!.Value;
            }

            this.ReconcileBreaks(workingDay, requestedDay.Breaks);
        }
    }

    /// <summary> Согласует перерывы дня графика с запрошенными значениями. </summary>
    private void ReconcileBreaks(OrganizationWorkingDay workingDay, List<OrganizationWorkingDayBreakDto> requestedBreaks)
    {
        var existingByStart = workingDay.Breaks.ToDictionary(value => value.StartTime);
        var requestedByStart = requestedBreaks.ToDictionary(value => value.StartTime);
        foreach (var existingBreak in workingDay.Breaks
            .Where(value => !requestedByStart.ContainsKey(value.StartTime))
            .ToList())
        {
            context.OrganizationWorkingDayBreaks.Remove(existingBreak);
        }

        foreach (var requestedBreak in requestedBreaks)
        {
            if (existingByStart.TryGetValue(requestedBreak.StartTime, out var existingBreak))
            {
                existingBreak.EndTime = requestedBreak.EndTime;
                continue;
            }

            workingDay.Breaks.Add(new OrganizationWorkingDayBreak
            {
                OrganizationId = workingDay.OrganizationId,
                DayOfWeek = workingDay.DayOfWeek,
                StartTime = requestedBreak.StartTime,
                EndTime = requestedBreak.EndTime
            });
        }
    }
}
