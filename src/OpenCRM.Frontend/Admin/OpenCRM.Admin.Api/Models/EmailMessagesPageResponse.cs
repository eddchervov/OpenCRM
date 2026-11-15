namespace OpenCRM.Admin.Api.Models;

/// <summary> Страница списка писем журнала. </summary>
public sealed class EmailMessagesPageResponse
{
    /// <summary> Элементы списка писем на странице. </summary>
    public IReadOnlyList<EmailMessageListItem> Items { get; init; } = [];

    /// <summary> Общее количество писем. </summary>
    public int TotalCount { get; init; }

    /// <summary> Номер страницы. </summary>
    public int Page { get; init; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; }
}
