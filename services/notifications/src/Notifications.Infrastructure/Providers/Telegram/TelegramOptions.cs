namespace Notifications.Infrastructure.Providers.Telegram;

/// <summary>Содержит параметры отправки сообщений владельцу через Telegram.</summary>
public sealed class TelegramOptions
{
    /// <summary>Имя раздела конфигурации.</summary>
    public const string SectionName = "Telegram";

    /// <summary>Токен Telegram-бота.</summary>
    public string BotToken { get; init; } = string.Empty;

    /// <summary>Адрес шлюза отправки через Telegram при недоступности Bot API напрямую.</summary>
    public string RelayUrl { get; init; } = string.Empty;

    /// <summary>Ключ доступа к шлюзу Telegram.</summary>
    public string RelayKey { get; init; } = string.Empty;

    /// <summary>Идентификатор чата владельца.</summary>
    public string OwnerChatId { get; init; } = string.Empty;

    /// <summary>HTTPS-адрес приложения, возвращающего токен Telegram-бота.</summary>
    public string CredentialBaseUrl { get; init; } = string.Empty;

    /// <summary>Ключ доступа к приложению, возвращающему токен бота.</summary>
    public string CredentialKey { get; init; } = string.Empty;

    /// <summary>Тайм-аут запроса к Telegram в секундах.</summary>
    public int RequestTimeoutSeconds { get; init; }
}
