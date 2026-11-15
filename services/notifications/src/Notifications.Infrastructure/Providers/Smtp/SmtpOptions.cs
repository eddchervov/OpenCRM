namespace Notifications.Infrastructure.Providers.Smtp;

/// <summary>
/// Содержит параметры подключения к SMTP-серверу.
/// </summary>
public sealed class SmtpOptions
{
    /// <summary>Имя раздела конфигурации.</summary>
    public const string SectionName = "Smtp";

    /// <summary>Имя или адрес SMTP-сервера.</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>TCP-порт SMTP-сервера.</summary>
    public int Port { get; init; }

    /// <summary>Использует TLS сразу при установлении соединения вместо STARTTLS.</summary>
    public bool UseSsl { get; init; }

    /// <summary>Имя пользователя для SMTP-аутентификации.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Пароль для SMTP-аутентификации.</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Адрес отправителя, отображаемый в письме.</summary>
    public string FromAddress { get; init; } = string.Empty;

    /// <summary>Отображаемое имя отправителя.</summary>
    public string FromName { get; init; } = string.Empty;
}
