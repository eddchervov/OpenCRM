namespace OpenCRM.Admin.Api.Models;

public sealed class AdminEmailCampaignScheduleRequest : AdminEmailRecipientRequest
{
    public string Subject { get; init; } = string.Empty;
    public string HtmlBody { get; init; } = string.Empty;
    public int IntervalSeconds { get; init; } = 60;
    public int StartDelayMinutes { get; init; }
}
