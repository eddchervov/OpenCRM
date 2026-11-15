namespace OpenCRM.Admin.Api.Models;

public sealed class AdminSiteNotificationTemplateRequest
{
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
}
