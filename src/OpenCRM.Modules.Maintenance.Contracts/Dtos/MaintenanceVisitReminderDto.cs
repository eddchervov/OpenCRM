namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Предпросмотр напоминания и фактическое состояние отправки. </summary>
public sealed record MaintenanceVisitReminderDto(
    bool Enabled,
    uint Version,
    DateTime? StartsAtUtc,
    DateTime? ScheduledAtUtc,
    string StatusCode,
    string StatusText,
    string ChannelCode,
    string Preview,
    bool CanEdit);
