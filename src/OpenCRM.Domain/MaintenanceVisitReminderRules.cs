using OpenCRM.Domain.Enums;

namespace OpenCRM.Domain;

/// <summary> Правила времени и актуальности напоминаний о визите. </summary>
public static class MaintenanceVisitReminderRules
{
    public const string EventCode = "booking_reminder";

    public const string DefaultTemplate = "{organization}: напоминаем о записи на {date} в {time}. {vehicle}. Если планы изменились, ответьте на это сообщение.";

    public static DateTime? GetStartsAtUtc(Maintenance maintenance, string? timeZoneId)
    {
        if (!maintenance.PlannedDate.HasValue || !maintenance.PlannedStartTime.HasValue
            || string.IsNullOrWhiteSpace(timeZoneId)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone))
        {
            return null;
        }

        var time = maintenance.PlannedStartTime.Value;
        if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
        {
            return null;
        }

        var localTime = DateTime.SpecifyKind(maintenance.PlannedDate.Value.Date.Add(time), DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(localTime) || timeZone.IsAmbiguousTime(localTime))
        {
            return null;
        }

        return TimeZoneInfo.ConvertTimeToUtc(localTime, timeZone);
    }

    public static bool IsCurrent(
        Maintenance maintenance,
        Organization organization,
        DateTime? expectedStart,
        DateTime utcNow)
    {
        return !maintenance.IsDeleted && maintenance.Status == MaintenanceStatus.Planned
            && maintenance.VisitReminderEnabled && organization.VisitReminderHoursBefore.HasValue
            && !organization.IsDeleted && !organization.IsArchived && organization.IsActive
            && expectedStart.HasValue && expectedStart.Value > utcNow
            && expectedStart.Value.AddHours(-organization.VisitReminderHoursBefore.Value) <= utcNow
            && MaintenanceVisitReminderRules.GetStartsAtUtc(maintenance, organization.ShiftTimeZoneId) == expectedStart;
    }

    public static string GetNotificationKey(Guid maintenanceId, DateTime startsAtUtc, int hoursBefore, string channel)
    {
        return $"visit-reminder:{maintenanceId:N}:{startsAtUtc.Ticks}:{hoursBefore}:{channel}";
    }

    public static bool IsChannelEnabled(string channelCode, string channel, bool hasTelegram)
    {
        return channelCode == "both" || channelCode == channel
            || channelCode == "auto" && (channel == "telegram" ? hasTelegram : !hasTelegram);
    }
}
