namespace Notifications.Domain;

/// <summary>
/// Перечисляет поддерживаемые способы доставки уведомлений.
/// </summary>
public enum NotificationChannel
{
    /// <summary>Доставка электронного письма через SMTP.</summary>
    Email = 1,

    /// <summary>Доставка SMS через внешний SMS-провайдер.</summary>
    Sms = 2,

    /// <summary>Доставка сообщения владельцу через Telegram.</summary>
    Telegram = 3,

    /// <summary>Доставка сообщения клиенту через Telegram-бот организации.</summary>
    TelegramClient = 5,

    /// <summary>Доставка сообщения пользователю через MAX.</summary>
    Max = 4
}
