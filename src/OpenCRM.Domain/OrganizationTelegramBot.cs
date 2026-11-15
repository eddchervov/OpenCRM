namespace OpenCRM.Domain;

/// <summary> Telegram-бот организации. </summary>
public sealed class OrganizationTelegramBot
{
    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Организация, к которой привязан бот. </summary>
    public Organization Organization { get; set; } = null!;

    /// <summary> Имя пользователя Telegram-бота. </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary> Числовой идентификатор бота, подтверждённый Telegram getMe. </summary>
    public long? TelegramBotId { get; set; }

    /// <summary> Защищённый токен Telegram-бота. </summary>
    public string ProtectedToken { get; set; } = string.Empty;

    /// <summary> Защищённый секрет вебхука Telegram-бота. </summary>
    public string ProtectedWebhookSecret { get; set; } = string.Empty;

    /// <summary> Признак включения Telegram-бота. </summary>
    public bool IsEnabled { get; set; }

    /// <summary> Признак уведомлений о записях через бот. </summary>
    public bool NotifyBooking { get; set; } = true;

    /// <summary> Признак уведомлений о готовности через бот. </summary>
    public bool NotifyReady { get; set; } = true;

    /// <summary> Дата и время последнего изменения настроек бота. </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
