namespace OpenCRM.Domain;

/// <summary> Обращение в поддержку сервиса. </summary>
public class SupportMessage
{
    /// <summary> Идентификатор обращения в поддержку. </summary>
    public int Id { get; set; }

    /// <summary> Код типа обращения в поддержку. </summary>
    public string TypeCode { get; set; } = SupportMessageTypeCodes.Question;

    /// <summary> Код статуса обращения в поддержку. </summary>
    public string StatusCode { get; set; } = SupportMessageStatusCodes.New;

    /// <summary> Идентификатор пользователя, отправившего обращение. </summary>
    public string? UserId { get; set; }

    /// <summary> Идентификатор организации, связанной с обращением. </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary> Организация, связанная с обращением в поддержку. </summary>
    public Organization? Organization { get; set; }

    /// <summary> Имя автора обращения. </summary>
    public string? Name { get; set; }

    /// <summary> Email автора обращения. </summary>
    public string? Email { get; set; }

    /// <summary> Телефон автора обращения. </summary>
    public string? Phone { get; set; }

    /// <summary> Тема обращения в поддержку. </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary> Текст обращения в поддержку. </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary> Страница, с которой отправлено обращение. </summary>
    public string? SourcePage { get; set; }

    /// <summary> Хеш IP-адреса автора обращения. </summary>
    public string? IpHash { get; set; }

    /// <summary> User-Agent автора обращения. </summary>
    public string? UserAgent { get; set; }

    /// <summary> Комментарий администратора к обращению. </summary>
    public string? AdminComment { get; set; }

    /// <summary> Дата и время создания обращения. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время последнего обновления обращения. </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Дата и время закрытия обращения. </summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary> Записи переписки по обращению. </summary>
    public ICollection<SupportMessageEntry> Entries { get; set; } = new List<SupportMessageEntry>();

    /// <summary> Вложения исходного сообщения и переписки. </summary>
    public ICollection<SupportAttachment> Attachments { get; set; } = new List<SupportAttachment>();
}

/// <summary> Коды типов обращений в поддержку. </summary>
public static class SupportMessageTypeCodes
{
    /// <summary> Код типа обращения — вопрос. </summary>
    public const string Question = "question";

    /// <summary> Код типа обращения — предложение. </summary>
    public const string Suggestion = "suggestion";

    /// <summary> Код типа обращения — сообщение об ошибке. </summary>
    public const string Bug = "bug";

    /// <summary> Код типа обращения — вопрос по оплате. </summary>
    public const string Billing = "billing";

    /// <summary> Код типа обращения — сотрудничество. </summary>
    public const string Cooperation = "cooperation";

    /// <summary> Список всех кодов типов обращений. </summary>
    public static readonly IReadOnlyCollection<string> All =
    [
        Question,
        Suggestion,
        Bug,
        Billing,
        Cooperation
    ];
}

/// <summary> Коды статусов обращений в поддержку. </summary>
public static class SupportMessageStatusCodes
{
    /// <summary> Код статуса обращения — новое. </summary>
    public const string New = "new";

    /// <summary> Код статуса обращения — в работе. </summary>
    public const string InProgress = "in_progress";

    /// <summary> Код статуса обращения — закрыто. </summary>
    public const string Closed = "closed";

    /// <summary> Код статуса обращения — спам. </summary>
    public const string Spam = "spam";

    /// <summary> Список всех кодов статусов обращений. </summary>
    public static readonly IReadOnlyCollection<string> All =
    [
        New,
        InProgress,
        Closed,
        Spam
    ];
}
