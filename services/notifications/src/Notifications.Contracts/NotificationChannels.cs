using System.Text.Json;

namespace Notifications.Contracts;

/// <summary>
/// Содержит строковые идентификаторы каналов, используемые во внешнем API.
/// </summary>
public static class NotificationChannels
{
    /// <summary>Канал отправки электронной почты.</summary>
    public const string Email = "email";

    /// <summary>Канал отправки SMS-сообщений.</summary>
    public const string Sms = "sms";

    /// <summary>Канал отправки сообщений владельцу в Telegram.</summary>
    public const string Telegram = "telegram";

    /// <summary>Канал отправки сообщений клиентам через Telegram-бот организации.</summary>
    public const string TelegramClient = "telegram-client";

    /// <summary>Канал отправки сообщений пользователям в MAX.</summary>
    public const string Max = "max";
}
