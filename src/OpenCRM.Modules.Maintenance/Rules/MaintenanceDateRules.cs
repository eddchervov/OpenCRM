using OpenCRM.Domain.Enums;
using System.Globalization;

namespace OpenCRM.Modules.Maintenance.Rules;

/// <summary> Правила работы с датами и статусами обслуживания. </summary>
internal static class MaintenanceDateRules
{
    /// <summary> Приводит дату обслуживания к нормализованному виду. </summary>
    internal static DateTime? NormalizeMaintenanceDate(DateTime? value)
    {
        return MaintenanceReadModelRules.NormalizeMaintenanceDate(value);
    }

    /// <summary> Разбирает текст даты обслуживания в значение DateTime. </summary>
    internal static DateTime? ParseMaintenanceDateText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var raw = value.Trim();
        if (DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate))
        {
            return isoDate.Date;
        }

        if (DateTime.TryParseExact(raw, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ruDate))
        {
            return ruDate.Date;
        }

        return null;
    }

    /// <summary> Устанавливает статус и даты обслуживания с согласованием полей. </summary>
    internal static void ApplyStatusAndDates(MaintenanceEntity maintenance, MaintenanceStatus status, DateTime? plannedDate, DateTime? completedDate)
    {
        maintenance.Status = status;

        switch (status)
        {
            case MaintenanceStatus.Planned:
            case MaintenanceStatus.InProgress:
            case MaintenanceStatus.Cancelled:
                if (!plannedDate.HasValue)
                {
                    throw new InvalidOperationException("Для выбранного состояния укажите плановую дату обслуживания.");
                }

                maintenance.PlannedDate = MaintenanceDateRules.ToStoredMaintenanceDate(plannedDate.Value);
                maintenance.CompletedDate = null;
                maintenance.ReadyForHandoverAt = null;
                maintenance.HandedOverAt = null;
                maintenance.Date = maintenance.PlannedDate.Value;
                break;

            case MaintenanceStatus.Completed:
                if (!completedDate.HasValue)
                {
                    throw new InvalidOperationException("Для выполненного обслуживания укажите фактическую дату.");
                }

                maintenance.PlannedDate = plannedDate.HasValue ? MaintenanceDateRules.ToStoredMaintenanceDate(plannedDate.Value) : null;
                maintenance.CompletedDate = MaintenanceDateRules.ToStoredMaintenanceDate(completedDate.Value);
                maintenance.Date = maintenance.CompletedDate.Value;
                break;

            default:
                throw new InvalidOperationException("Указано неподдерживаемое состояние обслуживания.");
        }
    }

    /// <summary> Преобразует дату к виду, используемому при хранении обслуживания. </summary>
    internal static DateTime ToStoredMaintenanceDate(DateTime value)
    {
        return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    }

    /// <summary> Устанавливает планируемые время начала, окончания и дату окончания. </summary>
    internal static void ApplyPlannedTime(
        MaintenanceEntity maintenance,
        TimeSpan? plannedStartTime,
        TimeSpan? plannedEndTime,
        DateTime? plannedEndDate)
    {
        var startDate = (maintenance.PlannedDate ?? maintenance.Date).Date;
        var endDate = plannedEndDate.HasValue ? MaintenanceDateRules.ToStoredMaintenanceDate(plannedEndDate.Value) : startDate;
        if (endDate < startDate)
        {
            throw new InvalidOperationException("Дата окончания не может быть раньше даты начала.");
        }

        if (endDate > startDate && (!plannedStartTime.HasValue || !plannedEndTime.HasValue))
        {
            throw new InvalidOperationException("Для записи на несколько дней укажите время начала и окончания.");
        }

        if (!plannedStartTime.HasValue && !plannedEndTime.HasValue)
        {
            maintenance.PlannedStartTime = null;
            maintenance.PlannedEndTime = null;
            maintenance.PlannedEndDate = maintenance.PlannedDate.HasValue ? endDate : null;
            return;
        }

        if (!plannedStartTime.HasValue)
        {
            throw new InvalidOperationException("Укажите время начала записи.");
        }

        var start = plannedStartTime.Value;
        if (start < TimeSpan.Zero || start >= TimeSpan.FromDays(1)
            || plannedEndTime is { } time && (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1)))
        {
            throw new InvalidOperationException("Время должно быть в пределах выбранного дня.");
        }

        var finish = plannedEndTime.HasValue
            ? endDate.Add(plannedEndTime.Value)
            : startDate.Add(start).AddHours(1);
        if (finish <= startDate.Add(start))
        {
            throw new InvalidOperationException("Окончание записи должно быть позже начала.");
        }

        maintenance.PlannedStartTime = start;
        maintenance.PlannedEndTime = finish.TimeOfDay;
        maintenance.PlannedEndDate = finish.Date;
    }
}
