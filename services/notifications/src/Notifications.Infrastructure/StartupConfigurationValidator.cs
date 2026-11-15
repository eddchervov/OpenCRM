using System.Globalization;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Notifications.Infrastructure;

/// <summary>Проверяет обязательные настройки до запуска обработчиков и провайдеров.</summary>
public static class StartupConfigurationValidator
{
    /// <summary>Проверяет включённые интеграции; режим миграций требует только подключение к базе.</summary>
    public static void Validate(
        IConfiguration configuration,
        IHostEnvironment environment,
        bool migrationsOnly = false)
    {
        RequireValue(configuration, "ConnectionStrings:NotificationsDb");
        if (!environment.IsDevelopment())
        {
            RequireSecret(configuration, "ConnectionStrings:NotificationsDb");
        }

        if (migrationsOnly)
        {
            return;
        }

        RequireValue(configuration, "RabbitMq:Host");
        RequireValue(configuration, "RabbitMq:VirtualHost");
        RequireValue(configuration, "RabbitMq:Username");
        RequireValue(configuration, "RabbitMq:Password");
        RequirePort(configuration, "RabbitMq:Port");
        if (!environment.IsDevelopment())
        {
            RequireSecret(configuration, "RabbitMq:Password");
        }

        var logOnly = RequireBoolean(configuration, "Delivery:LogOnly");
        var emailEnabled = RequireBoolean(configuration, "Email:Enabled");
        var smsEnabled = RequireBoolean(configuration, "Sms:Enabled");
        var telegramEnabled = RequireBoolean(configuration, "Telegram:Enabled");
        var telegramClientEnabled = RequireBoolean(configuration, "TelegramClient:Enabled");
        var maxEnabled = RequireBoolean(configuration, "Max:Enabled");
        if (!emailEnabled && !smsEnabled && !telegramEnabled && !telegramClientEnabled && !maxEnabled)
        {
            throw new InvalidOperationException("At least one delivery channel must be enabled.");
        }

        if (emailEnabled)
        {
            RequireProvider(configuration, "Email", "smtp", "unisender-go");
            RequirePort(configuration, "Smtp:Port");
            RequireBoolean(configuration, "Smtp:UseSsl");
            if (!logOnly)
            {
                ValidateSmtp(configuration);
            }
        }

        if (smsEnabled)
        {
            RequireProvider(configuration, "Sms", "smsru");
            var baseUrl = RequireValue(configuration, "SmsRu:BaseUrl");
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException("SmsRu:BaseUrl must be an absolute HTTP(S) URL.");
            }

            var timeout = configuration.GetValue<int?>("SmsRu:RequestTimeoutSeconds");
            if (timeout is null or <= 0 or > 600)
            {
                throw new InvalidOperationException("SmsRu:RequestTimeoutSeconds must be between 1 and 600.");
            }

            if (!logOnly)
            {
                RequireSecret(configuration, "SmsRu:ApiKey");
            }
        }

        if (telegramEnabled)
        {
            RequireProvider(configuration, "Telegram", "telegram");
            var timeout = configuration.GetValue<int?>("Telegram:RequestTimeoutSeconds");
            if (timeout is null or <= 0 or > 600)
            {
                throw new InvalidOperationException("Telegram:RequestTimeoutSeconds must be between 1 and 600.");
            }

            var relayUrl = configuration["Telegram:RelayUrl"];
            if (!string.IsNullOrWhiteSpace(relayUrl) && !IsRelayUrl(relayUrl, "/sendMessage"))
            {
                throw new InvalidOperationException("Telegram:RelayUrl must be an HTTPS Worker URL ending in /sendMessage.");
            }

            if (!logOnly)
            {
                if (string.IsNullOrWhiteSpace(relayUrl))
                {
                    RequireSecret(configuration, "Telegram:BotToken");
                    var ownerChatId = RequireValue(configuration, "Telegram:OwnerChatId");
                    if (!long.TryParse(ownerChatId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var chatId)
                        || chatId == 0)
                    {
                        throw new InvalidOperationException("Telegram:OwnerChatId must be a non-zero integer.");
                    }
                }
                else
                {
                    RequireSecret(configuration, "Telegram:RelayKey");
                }
            }
        }

        if (telegramClientEnabled)
        {
            RequireProvider(configuration, "TelegramClient", "telegram");
            var credentialBaseUrl = RequireValue(configuration, "TelegramClient:CredentialBaseUrl");
            if (!Uri.TryCreate(credentialBaseUrl, UriKind.Absolute, out var credentialUri)
                || credentialUri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(credentialUri.UserInfo)
                || !string.IsNullOrEmpty(credentialUri.Query)
                || !string.IsNullOrEmpty(credentialUri.Fragment))
            {
                throw new InvalidOperationException(
                    "TelegramClient:CredentialBaseUrl must be an absolute HTTPS URL without credentials or query.");
            }

            var relayUrl = RequireValue(configuration, "TelegramClient:RelayUrl");
            if (!IsRelayUrl(relayUrl, "/sendOrganizationMessage"))
            {
                throw new InvalidOperationException(
                    "TelegramClient:RelayUrl must be an HTTPS Worker URL ending in /sendOrganizationMessage.");
            }

            var timeout = configuration.GetValue<int?>("TelegramClient:RequestTimeoutSeconds");
            if (timeout is null or <= 0 or > 600)
            {
                throw new InvalidOperationException("TelegramClient:RequestTimeoutSeconds must be between 1 and 600.");
            }

            if (!logOnly)
            {
                RequireSecret(configuration, "TelegramClient:CredentialKey");
                if (configuration["TelegramClient:CredentialKey"]!.Length < 32)
                {
                    throw new InvalidOperationException("TelegramClient:CredentialKey must contain at least 32 characters.");
                }

                RequireSecret(configuration, "TelegramClient:RelayKey");
                if (configuration["TelegramClient:RelayKey"]!.Length < 32)
                {
                    throw new InvalidOperationException("TelegramClient:RelayKey must contain at least 32 characters.");
                }
            }
        }

        if (maxEnabled)
        {
            RequireProvider(configuration, "Max", "max");
            var credentialBaseUrl = RequireValue(configuration, "Max:CredentialBaseUrl");
            if (!Uri.TryCreate(credentialBaseUrl, UriKind.Absolute, out var credentialUri)
                || credentialUri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(credentialUri.UserInfo)
                || !string.IsNullOrEmpty(credentialUri.Query)
                || !string.IsNullOrEmpty(credentialUri.Fragment))
            {
                throw new InvalidOperationException("Max:CredentialBaseUrl must be an absolute HTTPS URL without credentials or query.");
            }

            var timeout = configuration.GetValue<int?>("Max:RequestTimeoutSeconds");
            if (timeout is null or <= 0 or > 600)
            {
                throw new InvalidOperationException("Max:RequestTimeoutSeconds must be between 1 and 600.");
            }

            if (!logOnly)
            {
                RequireSecret(configuration, "Max:CredentialKey");
                if (configuration["Max:CredentialKey"]!.Length < 32)
                {
                    throw new InvalidOperationException("Max:CredentialKey must contain at least 32 characters.");
                }
            }
        }
    }

    /// <summary>Возвращает обязательное значение без подстановки скрытых настроек.</summary>
    public static string RequireValue(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration '{key}' must be set.");
        }

        return value;
    }

    /// <summary>Проверяет выбранный адаптер; неизвестное имя не заменяется другим провайдером.</summary>
    public static void RequireProvider(IConfiguration configuration, string channel, params string[] supportedProviders)
    {
        var provider = RequireValue(configuration, $"{channel}:Provider");
        if (!supportedProviders.Contains(provider, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Provider for channel '{channel}' is not registered.");
        }
    }

    /// <summary> Проверяет наличие булевской настройки по ключу конфигурации. </summary>
    private static bool RequireBoolean(IConfiguration configuration, string key)
    {
        if (!bool.TryParse(RequireValue(configuration, key), out var value))
        {
            throw new InvalidOperationException($"Configuration '{key}' must be true or false.");
        }

        return value;
    }

    /// <summary> Проверяет корректность номера порта в конфигурации. </summary>
    private static void RequirePort(IConfiguration configuration, string key)
    {
        if (!int.TryParse(RequireValue(configuration, key), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"Configuration '{key}' must be a port between 1 and 65535.");
        }
    }

    /// <summary> Проверяет обязательные настройки SMTP. </summary>
    private static void ValidateSmtp(IConfiguration configuration)
    {
        RequireValue(configuration, "Smtp:Host");
        var fromAddress = RequireValue(configuration, "Smtp:FromAddress");
        if (!MailAddress.TryCreate(fromAddress, out _))
        {
            throw new InvalidOperationException("Smtp:FromAddress must be an email address.");
        }

        // SMTP relay может работать без аутентификации; заданный логин требует пароль.
        if (!string.IsNullOrWhiteSpace(configuration["Smtp:Username"]))
        {
            RequireSecret(configuration, "Smtp:Password");
        }
    }

    /// <summary> Проверяет наличие обязательного секретного значения. </summary>
    private static void RequireSecret(IConfiguration configuration, string key)
    {
        var value = RequireValue(configuration, key);
        if (value.Contains("change-me", StringComparison.OrdinalIgnoreCase)
            || value.Contains("development-", StringComparison.OrdinalIgnoreCase)
            || value.Contains("replace-with-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Configuration '{key}' contains a placeholder.");
        }
    }

    /// <summary> Проверяет совпадение URL релея с ожидаемым путём. </summary>
    private static bool IsRelayUrl(string value, string expectedPath)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.AbsolutePath == expectedPath
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }
}
