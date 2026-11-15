namespace OpenCRM.Admin.Api.Models;

public sealed class AdminSmsHistoryItem
{
    public int Id { get; init; }
    public string Text { get; init; } = string.Empty;
    public DateTime DateTime { get; init; }
    public string? Response { get; init; }
    public string SmsCode { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public int Type { get; init; }
}
