namespace OpenCRM.Admin.Api.Models;

public sealed class AdminEmailRecipientPreviewItem
{
    public string? UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}
