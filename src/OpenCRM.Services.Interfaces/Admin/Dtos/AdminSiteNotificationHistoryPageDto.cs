namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Страница истории уведомлений сайта. </summary>
public sealed class AdminSiteNotificationHistoryPageDto
{
    /// <summary> Уведомления на странице. </summary>
    public IReadOnlyList<AdminSiteNotificationDto> Items { get; init; } = [];

    /// <summary> Общее количество уведомлений в истории. </summary>
    public int TotalCount { get; init; }

    /// <summary> Номер страницы. </summary>
    public int Page { get; init; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; init; }
}
