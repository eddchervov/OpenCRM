using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using System.Globalization;

namespace OpenCRM.Modules.Maintenance.Rules;

/// <summary> Правила применения рабочих действий к обслуживанию. </summary>
internal sealed class MaintenanceWorkflowRules(
    ISystemClock systemClock)
{
    /// <summary> Нормализует код рабочего действия. </summary>
    internal static string NormalizeWorkflowAction(string? action)
    {
        var normalized = action?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "start" => "start",
            "complete" => "complete",
            "cancel" => "cancel",
            "reschedule" => "reschedule",
            "reopen" => "reopen",
            "return_to_planned" => "return_to_planned",
            "return_to_work" => "return_to_work",
            "mark_ready" => "mark_ready",
            "handover" => "handover",
            "simple_handover" => "simple_handover",
            "undo_handover" => "undo_handover",
            _ => throw new InvalidOperationException("Указано неподдерживаемое действие обслуживания.")
        };
    }

    /// <summary> Применяет рабочее действие к обслуживанию и возвращает описание. </summary>
    internal string ApplyWorkflowAction(
        MaintenanceEntity maintenance,
        string action,
        MaintenanceWorkflowActionDto dto,
        bool isOrganizationWorkspace,
        bool isSimpleWorkflow = false,
        TimeZoneInfo? organizationTimeZone = null)
    {
        if (!isOrganizationWorkspace && action is ("mark_ready" or "handover" or "simple_handover" or "undo_handover"))
        {
            throw new InvalidOperationException("Выдача доступна только в рабочем пространстве организации.");
        }

        if (action == "simple_handover" && !isSimpleWorkflow)
        {
            throw new InvalidOperationException("Выдача одним действием доступна только в простом режиме детейлинга и СТО.");
        }

        switch (action)
        {
            case "start":
                if (maintenance.Status != MaintenanceStatus.Planned)
                {
                    throw new InvalidOperationException("В работу можно взять только запланированную запись.");
                }

                MaintenanceDateRules.ApplyStatusAndDates(maintenance, MaintenanceStatus.InProgress, maintenance.PlannedDate ?? maintenance.Date, null);
                return "Обслуживание взято в работу";

            case "complete":
                if (maintenance.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled)
                {
                    throw new InvalidOperationException("Выполнить можно только запланированное обслуживание или обслуживание в работе.");
                }

                MaintenanceDateRules.ApplyStatusAndDates(
                    maintenance,
                    MaintenanceStatus.Completed,
                    maintenance.PlannedDate,
                    MaintenanceDateRules.ParseMaintenanceDateText(dto.CompletedDateText)
                        ?? MaintenanceDateRules.NormalizeMaintenanceDate(dto.CompletedDate)
                        ?? this.GetCurrentDate(organizationTimeZone));
                maintenance.Mileage = dto.Mileage ?? maintenance.Mileage;
                maintenance.ReadyForHandoverAt = isOrganizationWorkspace ? systemClock.UtcNow : null;

                return isOrganizationWorkspace ? "Работы завершены: техника готова к выдаче" : "Обслуживание выполнено";

            case "mark_ready":
                if (maintenance.Status != MaintenanceStatus.Completed || maintenance.ReadyForHandoverAt.HasValue)
                {
                    throw new InvalidOperationException("Отметить готовность можно только у выполненной записи без отметки выдачи.");
                }

                maintenance.ReadyForHandoverAt = systemClock.UtcNow;

                return "Техника отмечена готовой к выдаче";

            case "handover":
                if (maintenance.Status != MaintenanceStatus.Completed || !maintenance.ReadyForHandoverAt.HasValue || maintenance.HandedOverAt.HasValue)
                {
                    throw new InvalidOperationException("Выдать можно только технику, готовую к выдаче. Обновите карточку.");
                }

                maintenance.HandedOverAt = systemClock.UtcNow;

                return "Техника выдана клиенту";

            case "simple_handover":
                if (maintenance.Status is MaintenanceStatus.Cancelled || maintenance.HandedOverAt.HasValue)
                {
                    throw new InvalidOperationException("Выдать можно только незавершённую запись без предыдущей выдачи.");
                }

                var handedOverAt = systemClock.UtcNow;
                if (maintenance.Status != MaintenanceStatus.Completed)
                {
                    MaintenanceDateRules.ApplyStatusAndDates(
                        maintenance,
                        MaintenanceStatus.Completed,
                        maintenance.PlannedDate ?? maintenance.Date,
                        this.GetCurrentDate(organizationTimeZone, handedOverAt));
                    maintenance.ReadyForHandoverAt = handedOverAt;
                }
                else
                {
                    maintenance.ReadyForHandoverAt ??= handedOverAt;
                }

                maintenance.HandedOverAt = handedOverAt;

                return "Работы завершены и техника выдана клиенту";

            case "undo_handover":
                if (maintenance.Status != MaintenanceStatus.Completed || !maintenance.HandedOverAt.HasValue)
                {
                    throw new InvalidOperationException("Отменить выдачу можно только у выданной техники.");
                }

                if (string.IsNullOrWhiteSpace(dto.Reason))
                {
                    throw new InvalidOperationException("Укажите причину отмены выдачи.");
                }

                maintenance.HandedOverAt = null;

                return "Выдача отменена: техника снова ожидает клиента";

            case "return_to_planned":
                if (maintenance.Status != MaintenanceStatus.InProgress)
                {
                    throw new InvalidOperationException("Вернуть в запланированные можно только запись в работе.");
                }

                MaintenanceDateRules.ApplyStatusAndDates(maintenance, MaintenanceStatus.Planned, maintenance.PlannedDate ?? maintenance.Date, null);
                return "Запись возвращена в запланированные";

            case "return_to_work":
                if (maintenance.Status != MaintenanceStatus.Completed)
                {
                    throw new InvalidOperationException("Вернуть в работу можно только выполненную запись.");
                }

                if (maintenance.HandedOverAt.HasValue)
                {
                    throw new InvalidOperationException("Сначала отмените выдачу техники, затем верните запись в работу.");
                }

                MaintenanceDateRules.ApplyStatusAndDates(maintenance, MaintenanceStatus.InProgress, maintenance.PlannedDate ?? maintenance.Date, null);
                return "Выполнение отменено: запись возвращена в работу";

            case "cancel":
                if (maintenance.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled)
                {
                    throw new InvalidOperationException("Отменить можно только запланированное обслуживание или обслуживание в работе.");
                }

                MaintenanceDateRules.ApplyStatusAndDates(maintenance, MaintenanceStatus.Cancelled, maintenance.PlannedDate ?? maintenance.Date, null);
                return "Обслуживание отменено";

            case "reschedule":
                if (maintenance.Status is MaintenanceStatus.Completed or MaintenanceStatus.Cancelled)
                {
                    throw new InvalidOperationException("Перенести можно только запланированное обслуживание или обслуживание в работе.");
                }

                if (!dto.PlannedDate.HasValue)
                {
                    throw new InvalidOperationException("Укажите новую плановую дату обслуживания.");
                }

                MaintenanceDateRules.ApplyStatusAndDates(maintenance, maintenance.Status, MaintenanceDateRules.ParseMaintenanceDateText(dto.PlannedDateText) ?? MaintenanceDateRules.NormalizeMaintenanceDate(dto.PlannedDate), null);
                MaintenanceWorkflowRules.ApplyWorkflowPlannedTime(maintenance, dto, organizationTimeZone);
                return "Обслуживание перенесено";

            case "reopen":
                if (maintenance.Status != MaintenanceStatus.Cancelled)
                {
                    throw new InvalidOperationException("Возобновить можно только отмененное обслуживание.");
                }

                var reopenPlannedDate = MaintenanceDateRules.ParseMaintenanceDateText(dto.PlannedDateText) ?? MaintenanceDateRules.NormalizeMaintenanceDate(dto.PlannedDate);
                if (!reopenPlannedDate.HasValue)
                {
                    throw new InvalidOperationException("Укажите новую плановую дату обслуживания.");
                }

                MaintenanceDateRules.ApplyStatusAndDates(maintenance, MaintenanceStatus.Planned, reopenPlannedDate, null);
                MaintenanceWorkflowRules.ApplyWorkflowPlannedTime(maintenance, dto, organizationTimeZone);
                return "Обслуживание возобновлено";

            default:
                throw new InvalidOperationException("Указано неподдерживаемое действие обслуживания.");
        }
    }

    private DateTime GetCurrentDate(TimeZoneInfo? timeZone, DateTime? utcNow = null)
    {
        var currentTime = utcNow ?? systemClock.UtcNow;
        if (timeZone == null)
        {
            return currentTime.Date;
        }

        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(currentTime, DateTimeKind.Utc), timeZone).Date;
    }

    /// <summary> Устанавливает планируемое время из данных рабочего действия. </summary>
    internal static void ApplyWorkflowPlannedTime(
        MaintenanceEntity maintenance,
        MaintenanceWorkflowActionDto dto,
        TimeZoneInfo? organizationTimeZone = null)
    {
        var start = MaintenanceWorkflowRules.ParsePlannedTimeText(dto.PlannedStartTimeText) ?? dto.PlannedStartTime ?? maintenance.PlannedStartTime ?? new TimeSpan(9, 0, 0);
        var end = MaintenanceWorkflowRules.ParsePlannedTimeText(dto.PlannedEndTimeText) ?? dto.PlannedEndTime ?? maintenance.PlannedEndTime ?? start.Add(TimeSpan.FromHours(1));

        MaintenanceDateRules.ApplyPlannedTime(maintenance, start, end, dto.PlannedEndDate, organizationTimeZone);
    }

    /// <summary> Формирует описание рабочего действия для истории изменений. </summary>
    internal static string BuildWorkflowHistoryDescription(MaintenanceSnapshot previous, MaintenanceEntity maintenance, string? reason)
    {
        var changes = new List<string>();

        MaintenanceChangeRules.AddChange(changes, "Состояние", MaintenanceChangeRules.GetMaintenanceStatusText(previous.Status), MaintenanceChangeRules.GetMaintenanceStatusText(maintenance.Status));
        MaintenanceChangeRules.AddChange(changes, "Плановая дата", MaintenanceChangeRules.FormatDate(previous.PlannedDate), MaintenanceChangeRules.FormatDate(maintenance.PlannedDate));
        MaintenanceChangeRules.AddChange(changes, "Плановая дата окончания", MaintenanceChangeRules.FormatDate(previous.PlannedEndDate), MaintenanceChangeRules.FormatDate(maintenance.PlannedEndDate));
        MaintenanceChangeRules.AddChange(changes, "Время начала", MaintenanceChangeRules.FormatTime(previous.PlannedStartTime), MaintenanceChangeRules.FormatTime(maintenance.PlannedStartTime));
        MaintenanceChangeRules.AddChange(changes, "Время окончания", MaintenanceChangeRules.FormatTime(previous.PlannedEndTime), MaintenanceChangeRules.FormatTime(maintenance.PlannedEndTime));
        MaintenanceChangeRules.AddChange(changes, "Пост / место", previous.Location, maintenance.Location);
        MaintenanceChangeRules.AddChange(changes, "Дата выполнения", MaintenanceChangeRules.FormatDate(previous.CompletedDate), MaintenanceChangeRules.FormatDate(maintenance.CompletedDate));
        MaintenanceChangeRules.AddChange(changes, "Готовность к выдаче (UTC)", previous.ReadyForHandoverAt?.ToString("O"), maintenance.ReadyForHandoverAt?.ToString("O"));
        MaintenanceChangeRules.AddChange(changes, "Выдача (UTC)", previous.HandedOverAt?.ToString("O"), maintenance.HandedOverAt?.ToString("O"));
        MaintenanceChangeRules.AddChange(changes, "Пробег/наработка", MaintenanceChangeRules.FormatNullableNumber(previous.Mileage), MaintenanceChangeRules.FormatNullableNumber(maintenance.Mileage));

        if (!string.IsNullOrWhiteSpace(reason))
        {
            changes.Add($"Комментарий: {reason.Trim()}");
        }

        return string.Join("\n", changes);
    }

    /// <summary> Разбирает текст планируемого времени в TimeSpan. </summary>
    internal static TimeSpan? ParsePlannedTimeText(string? value)
    {
        return TimeSpan.TryParseExact(value, new[] { @"hh\:mm", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    /// <summary> Проверяет допустимость смены статуса в рабочем пространстве. </summary>
    internal static void ValidateWorkspaceStatusChange(MaintenanceStatus previous, MaintenanceStatus next)
    {
        var allowed = previous == next || (previous, next) is
            (MaintenanceStatus.Planned, MaintenanceStatus.InProgress) or
            (MaintenanceStatus.Planned, MaintenanceStatus.Cancelled) or
            (MaintenanceStatus.InProgress, MaintenanceStatus.Cancelled) or
            (MaintenanceStatus.Cancelled, MaintenanceStatus.Planned);
        if (!allowed)
        {
            throw new InvalidOperationException("Недопустимый переход состояния. Для завершения записи используйте действие «Выполнено» в карточке.");
        }
    }
}
