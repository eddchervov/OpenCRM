namespace OpenCRM.Domain;

/// <summary> Настройки Max-бота организации. </summary>
public sealed class OrganizationMaxBot
{
    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Организация, к которой относятся настройки бота. </summary>
    public Organization Organization { get; set; } = null!;

    /// <summary> Имя пользователя Max-бота. </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary> Идентификатор бота, подтверждённый MAX после проверки токена. </summary>
    public string? ImmutableProviderBotId { get; set; }

    /// <summary> Защищённый токен доступа Max-бота. </summary>
    public string ProtectedToken { get; set; } = string.Empty;

    /// <summary> Защищённый секрет вебхука Max-бота. </summary>
    public string ProtectedWebhookSecret { get; set; } = string.Empty;

    /// <summary> Признак включённого бота. </summary>
    public bool IsEnabled { get; set; }

    /// <summary> Признак уведомлений о новых записях на обслуживание. </summary>
    public bool NotifyBooking { get; set; } = true;

    /// <summary> Признак уведомлений о готовности техники. </summary>
    public bool NotifyReady { get; set; } = true;

    /// <summary> Дата и время последнего обновления настроек бота. </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
