using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.WebApp.Models.Admin;

/// <summary> Модель страницы сообщений поддержки в панели администратора. </summary>
public class SupportMessagesViewModel
{
    /// <summary> Фильтр по статусу сообщений. </summary>
    public string? Status { get; set; }

    /// <summary> Поисковая строка для фильтрации сообщений. </summary>
    public string? Search { get; set; }

    /// <summary> Список сообщений поддержки на странице. </summary>
    public IReadOnlyList<AdminSupportMessageDto> Items { get; set; } = [];

    /// <summary> Количество новых сообщений. </summary>
    public int NewCount { get; set; }

    /// <summary> Количество сообщений в работе. </summary>
    public int InProgressCount { get; set; }

    /// <summary> Количество закрытых сообщений. </summary>
    public int ClosedCount { get; set; }

    /// <summary> Количество сообщений, помеченных как спам. </summary>
    public int SpamCount { get; set; }

    /// <summary> Доступные варианты статусов сообщений. </summary>
    public IReadOnlyList<SupportStatusOption> StatusOptions { get; } =
    [
        new(SupportMessageStatusCodes.New, "Новые"),
        new(SupportMessageStatusCodes.InProgress, "В работе"),
        new(SupportMessageStatusCodes.Closed, "Закрытые"),
        new(SupportMessageStatusCodes.Spam, "Спам")
    ];
}

/// <summary> Вариант статуса сообщения поддержки. </summary>
public sealed record SupportStatusOption(string Code, string Name);

/// <summary> Модель обновления сообщения поддержки администратором. </summary>
public class UpdateSupportMessageAdminModel
{
    /// <summary> Идентификатор сообщения. </summary>
    public int Id { get; set; }

    /// <summary> Код нового статуса сообщения. </summary>
    public string StatusCode { get; set; } = SupportMessageStatusCodes.InProgress;

    /// <summary> Комментарий администратора к сообщению. </summary>
    public string? AdminComment { get; set; }
}
