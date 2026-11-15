using OpenCRM.Domain;

namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Элемент списка писем в журнале. </summary>
public class EmailMessageListItem
{
    /// <summary> Идентификатор письма. </summary>
    public int Id { get; init; }

    /// <summary> Email получателя. </summary>
    public string ToEmail { get; init; } = string.Empty;

    /// <summary> Тема письма. </summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary> Код статуса письма. </summary>
    public string StatusCode { get; init; } = EmailMessageStatusCodes.Pending;

    /// <summary> Идентификатор пользователя, если сопоставлен. </summary>
    public string? UserId { get; init; }

    /// <summary> Отображаемое имя пользователя, если известно. </summary>
    public string? UserDisplayName { get; init; }

    /// <summary> Время создания письма. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Время отправки письма, если отправлено. </summary>
    public DateTime? SentAt { get; init; }

    /// <summary> Текст ошибки отправки, если была. </summary>
    public string? ErrorMessage { get; init; }

    /// <summary> Имя провайдера отправки, если известен. </summary>
    public string? Provider { get; init; }

    /// <summary> Идентификатор письма у провайдера, если известен. </summary>
    public string? ProviderMessageId { get; init; }

    /// <summary> Ответ провайдера, если сохранён. </summary>
    public string? ProviderResponse { get; init; }

    /// <summary> Код последнего события письма, если был. </summary>
    public string? LastEventCode { get; init; }

    /// <summary> Время последнего события письма, если было. </summary>
    public DateTime? LastEventAt { get; init; }

    /// <summary> Количество открытий письма. </summary>
    public int OpenCount { get; init; }

    /// <summary> Количество переходов по ссылкам письма. </summary>
    public int ClickCount { get; init; }

    /// <summary> Общее количество событий письма. </summary>
    public int EventCount { get; init; }
}
