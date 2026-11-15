namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO страницы сообщений поддержки. </summary>
public class AdminSupportMessagesPageDto
{
    /// <summary> Фильтр по статусу сообщений. </summary>
    public string? Status { get; set; }

    /// <summary> Поисковая строка фильтра. </summary>
    public string? Search { get; set; }

    /// <summary> Список сообщений страницы. </summary>
    public IReadOnlyList<AdminSupportMessageDto> Items { get; set; } = [];

    /// <summary> Общее количество сообщений. </summary>
    public int TotalCount { get; set; }

    /// <summary> Размер страницы. </summary>
    public int PageSize { get; set; } = 50;

    /// <summary> Количество новых сообщений. </summary>
    public int NewCount { get; set; }

    /// <summary> Количество сообщений в работе. </summary>
    public int InProgressCount { get; set; }

    /// <summary> Количество закрытых сообщений. </summary>
    public int ClosedCount { get; set; }

    /// <summary> Количество сообщений, отмеченных как спам. </summary>
    public int SpamCount { get; set; }
}
