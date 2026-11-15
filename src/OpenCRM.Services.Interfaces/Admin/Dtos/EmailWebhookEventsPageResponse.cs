namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Страница webhook-событий писем. </summary>
public sealed class EmailWebhookEventsPageResponse : EmailMessageEventsResponse
{
    /// <summary> Общее количество webhook-событий. </summary>
    public int TotalCount { get; init; }

    /// <summary> Номер страницы. </summary>
    public int Page { get; init; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; }
}
