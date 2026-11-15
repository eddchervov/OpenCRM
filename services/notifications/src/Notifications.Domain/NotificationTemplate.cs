namespace Notifications.Domain;

/// <summary>Неизменяемая версия шаблона, принадлежащая приложению и каналу.</summary>
public sealed class NotificationTemplate
{
    /// <summary> Создаёт экземпляр для инфраструктуры. </summary>
    private NotificationTemplate()
    {
    }

    /// <summary>Создаёт активную версию шаблона.</summary>
    public NotificationTemplate(
        string applicationId,
        string key,
        NotificationChannel channel,
        string? subjectTemplate,
        string bodyTemplate,
        bool isHtml,
        DateTimeOffset updatedAt,
        int version = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyTemplate);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        this.ApplicationId = applicationId.Trim();
        this.Key = key.Trim();
        this.Channel = channel;
        this.SubjectTemplate = subjectTemplate;
        this.BodyTemplate = bodyTemplate;
        this.IsHtml = isHtml;
        this.UpdatedAt = updatedAt;
        this.Version = version;
    }

    /// <summary>Приложение-владелец шаблона.</summary>
    public string ApplicationId { get; private set; } = string.Empty;

    /// <summary>Бизнес-ключ шаблона внутри приложения и канала.</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>Канал доставки.</summary>
    public NotificationChannel Channel { get; private set; }

    /// <summary>Положительный номер неизменяемой версии.</summary>
    public int Version { get; private set; }

    /// <summary>Шаблон темы электронного письма.</summary>
    public string? SubjectTemplate { get; private set; }

    /// <summary>Шаблон основного текста сообщения.</summary>
    public string BodyTemplate { get; private set; } = string.Empty;

    /// <summary>Признак HTML-содержимого.</summary>
    public bool IsHtml { get; private set; }

    /// <summary>Разрешение использовать шаблон для новых уведомлений.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Момент регистрации содержимого версии.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }
}
