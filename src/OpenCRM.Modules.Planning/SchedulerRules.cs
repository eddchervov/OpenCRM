using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Planning;

/// <summary> Правила нормализации и расчётов планировщика. </summary>
internal static class SchedulerRules
{
    /// <summary> Обрезает и очищает необязательную строку. </summary>
    internal static string? NormalizeNullable(string? value)
    {
        var normalized = value?.Trim();

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    /// <summary> Нормализует обязательную строку или выбрасывает ошибку. </summary>
    internal static string NormalizeRequired(string? value, string errorMessage)
    {
        var normalized = value?.Trim();

        return string.IsNullOrWhiteSpace(normalized) ? throw new InvalidOperationException(errorMessage) : normalized;
    }

    /// <summary> Нормализует строку фильтра. </summary>
    internal static string NormalizeFilter(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary> Нормализует значение даты и времени. </summary>
    internal static DateTime? NormalizeDateTime(DateTime? value) => value?.ToUniversalTime();

    /// <summary> Нормализует интервал повторения. </summary>
    internal static int? NormalizeRecurrenceInterval(int? interval) => interval.HasValue ? Math.Clamp(interval.Value, 1, 365) : null;

    /// <summary> Нормализует даты задачи планировщика. </summary>
    internal static void NormalizeTaskDates(OrganizationSchedulerTask task)
    {
        if (task.StartAt.HasValue && task.EndAt.HasValue && task.EndAt.Value <= task.StartAt.Value)
        {
            task.EndAt = task.StartAt.Value.AddHours(1);
        }

        if (!task.StartAt.HasValue && task.EndAt.HasValue)
        {
            task.StartAt = task.EndAt.Value.Date.AddHours(9);
        }
    }

    /// <summary> Нормализует параметры повторения задачи. </summary>
    internal static void NormalizeTaskRecurrence(OrganizationSchedulerTask task)
    {
        if (task.RecurrenceCode == OrganizationSchedulerTaskRecurrenceCodes.None)
        {
            task.RecurrenceInterval = null;
            task.RecurrenceUntil = null;
            task.RecurrenceGeneratedUntil = null;
            return;
        }

        if (!task.StartAt.HasValue)
        {
            throw new InvalidOperationException("Для повторяющейся задачи укажите дату и время начала.");
        }

        task.RecurrenceInterval = Math.Clamp(task.RecurrenceInterval ?? 1, 1, 365);
    }

    /// <summary> Вычисляет дату следующего начала повторения по коду и интервалу. </summary>
    internal static DateTime NextRecurrenceStart(DateTime currentStart, string recurrenceCode, int interval)
    {
        var safeInterval = Math.Clamp(interval, 1, 365);

        return OrganizationSchedulerTaskRecurrenceCodes.Normalize(recurrenceCode) switch
        {
            OrganizationSchedulerTaskRecurrenceCodes.Daily => currentStart.AddDays(safeInterval),
            OrganizationSchedulerTaskRecurrenceCodes.Weekly => currentStart.AddDays(7 * safeInterval),
            OrganizationSchedulerTaskRecurrenceCodes.Monthly => currentStart.AddMonths(Math.Clamp(safeInterval, 1, 120)),
            _ => currentStart
        };
    }

    /// <summary> Преобразует статус обслуживания в код статуса задачи. </summary>
    internal static string MapSchedulerTaskStatusCode(MaintenanceStatus maintenanceStatus) => maintenanceStatus switch
    {
        MaintenanceStatus.Completed => OrganizationSchedulerTaskStatusCodes.Closed,
        MaintenanceStatus.Cancelled => OrganizationSchedulerTaskStatusCodes.Closed,
        MaintenanceStatus.Planned => OrganizationSchedulerTaskStatusCodes.InProgress,
        MaintenanceStatus.InProgress => OrganizationSchedulerTaskStatusCodes.InProgress,
        _ => OrganizationSchedulerTaskStatusCodes.New
    };

    /// <summary> Возвращает CSS-класс бейджа по коду статуса задачи. </summary>
    internal static string GetTaskStatusBadgeClass(string? statusCode) => OrganizationSchedulerTaskStatusCodes.Normalize(statusCode) switch
    {
        OrganizationSchedulerTaskStatusCodes.InProgress => "text-bg-warning",
        OrganizationSchedulerTaskStatusCodes.Deferred => "text-bg-secondary",
        OrganizationSchedulerTaskStatusCodes.Closed => "text-bg-success",
        _ => "text-bg-info"
    };

    /// <summary> Строит подзаголовок элемента календаря из данных техники и клиента. </summary>
    internal static string? BuildCalendarSubtitle(string? vehicleTitle, string? vehicleName, string? clientName)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(vehicleTitle))
        {
            parts.Add(vehicleTitle.Trim());
        }
        if (!string.IsNullOrWhiteSpace(vehicleName))
        {
            parts.Add(vehicleName.Trim());
        }
        if (!string.IsNullOrWhiteSpace(clientName))
        {
            parts.Add(clientName.Trim());
        }

        return parts.Count == 0 ? null : string.Join(" • ", parts);
    }

    /// <summary> Формирует комментарий элемента календаря из задачи и реквизитов. </summary>
    internal static string BuildCalendarComment(string? comment, string recurrenceCode, DateTime? reminderAt, Guid? defectId, Guid? maintenanceId)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(comment))
        {
            parts.Add(comment.Trim());
        }
        if (OrganizationSchedulerTaskRecurrenceCodes.Normalize(recurrenceCode) != OrganizationSchedulerTaskRecurrenceCodes.None)
        {
            parts.Add(OrganizationSchedulerTaskRecurrenceCodes.GetName(recurrenceCode));
        }
        if (reminderAt.HasValue)
        {
            parts.Add($"Напомнить {reminderAt.Value:dd.MM.yyyy HH:mm}");
        }
        if (defectId.HasValue)
        {
            parts.Add("Связана с неисправностью");
        }
        if (maintenanceId.HasValue)
        {
            parts.Add("Связана с обслуживанием");
        }

        return string.Join(" • ", parts);
    }
}
