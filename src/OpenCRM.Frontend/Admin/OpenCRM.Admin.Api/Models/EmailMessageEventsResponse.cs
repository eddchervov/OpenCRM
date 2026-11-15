namespace OpenCRM.Admin.Api.Models;

/// <summary> Список событий письма. </summary>
public class EmailMessageEventsResponse
{
    /// <summary> Элементы списка событий. </summary>
    public IReadOnlyList<EmailMessageEventItem> Items { get; init; } = [];
}
