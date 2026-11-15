namespace OpenCRM.Admin.Api.Models;

public sealed class AdminEmailRecipientsPreview
{
    public int TotalCount { get; init; }
    public IReadOnlyList<AdminEmailRecipientPreviewItem> Items { get; init; } = [];
}
