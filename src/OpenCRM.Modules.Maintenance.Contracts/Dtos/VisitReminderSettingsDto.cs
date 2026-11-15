namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Настройки напоминаний о визите в организации. </summary>
public sealed class VisitReminderSettingsDto
{
    public int? HoursBefore { get; set; }

    public string ChannelCode { get; set; } = "auto";

    public string Template { get; set; } = string.Empty;

    public string? TimeZoneId { get; set; }
}
