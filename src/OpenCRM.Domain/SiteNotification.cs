namespace OpenCRM.Domain;

/// <summary> Уведомление на сайте. </summary>
public sealed class SiteNotification
{
    /// <summary> Идентификатор уведомления. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Заголовок уведомления. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Текст уведомления. </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary> Битовая маска аудитории уведомления. </summary>
    public int AudienceMask { get; set; }

    /// <summary> Код типа уведомления. </summary>
    public string TypeCode { get; set; } = "manual";

    /// <summary> Идентификатор пользователя-получателя. </summary>
    public string? RecipientUserId { get; set; }

    /// <summary> Признак шаблона уведомления. </summary>
    public bool IsTemplate { get; set; }

    /// <summary> Ключ события, породившего уведомление. </summary>
    public string? EventKey { get; set; }

    /// <summary> Признак удаления уведомления. </summary>
    public bool IsDeleted { get; set; }

    /// <summary> Дата и время создания уведомления. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Записи закрытия уведомления пользователями. </summary>
    public ICollection<SiteNotificationClosure> Closures { get; set; } = new List<SiteNotificationClosure>();
}
