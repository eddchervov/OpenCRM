namespace OpenCRM.Admin.Api.Models;

public sealed class AdminSiteNotificationSendRequest
{
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string RecipientScope { get; init; } = "all";
    public IReadOnlyCollection<string> UserIds { get; init; } = [];
    public IReadOnlyCollection<string> Audiences { get; init; } = [];
}
