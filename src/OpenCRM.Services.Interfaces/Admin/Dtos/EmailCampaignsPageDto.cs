using OpenCRM.Domain;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Страница списка email-кампаний. </summary>
public sealed class EmailCampaignsPageDto
{
    /// <summary> Элементы списка кампаний на странице. </summary>
    public IReadOnlyList<EmailCampaignListItemDto> Items { get; init; } = [];

    /// <summary> Общее количество кампаний. </summary>
    public int TotalCount { get; init; }

    /// <summary> Номер страницы. </summary>
    public int Page { get; init; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; }
}

