namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary>
/// Предпросмотр напоминания и фактическое состояние отправки.
/// Код причины и часовой пояс позволяют локализовать описание, сохраняя исходный текст для прежних клиентов.
/// </summary>
public sealed record MaintenanceVisitReminderDto(
    bool Enabled,
    uint Version,
    DateTime? StartsAtUtc,
    DateTime? ScheduledAtUtc,
    string StatusCode,
    string StatusText,
    string ChannelCode,
    string Preview,
    bool CanEdit,
    string? TimeZoneId = null,
    string? StatusDetailCode = null);
