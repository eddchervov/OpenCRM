namespace OpenCRM.Admin.Api.Models;

public sealed class AdminSiteNotification
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string TypeCode { get; init; } = string.Empty;
    public bool IsTemplate { get; init; }
    public IReadOnlyCollection<string> Audiences { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public string? RecipientEmail { get; init; }
}
