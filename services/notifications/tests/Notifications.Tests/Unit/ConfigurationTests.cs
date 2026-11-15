using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using Notifications.Infrastructure;

namespace Notifications.Tests.Unit;

/// <summary>Проверяет отсутствие неявных настроек и раннее отклонение ошибочной конфигурации.</summary>
public sealed class ConfigurationTests
{
    /// <summary>Обязательные параметры нельзя удалить даже при разработке в LogOnly.</summary>
    [Theory]
    [InlineData("Delivery:LogOnly")]
    [InlineData("RabbitMq:Host")]
    [InlineData("RabbitMq:Port")]
    [InlineData("RabbitMq:VirtualHost")]
    [InlineData("RabbitMq:Username")]
    [InlineData("Email:Enabled")]
    [InlineData("Email:Provider")]
    [InlineData("Sms:Enabled")]
    [InlineData("Sms:Provider")]
    [InlineData("SmsRu:BaseUrl")]
    [InlineData("SmsRu:RequestTimeoutSeconds")]
    [InlineData("Telegram:Enabled")]
    [InlineData("TelegramClient:Enabled")]
    [InlineData("Max:Enabled")]
    public void MissingSettingFailsStartup(string key)
    {
        var values = CreateSettings();
        values.Remove(key);

        Assert.Throws<InvalidOperationException>(() => Validate(values));
    }

    /// <summary>Имитация не требует ключей реальных провайдеров.</summary>
    [Fact]
    public void LogOnlyAllowsMissingProviderCredentials()
    {
        Validate(CreateSettings());
    }

    /// <summary>SMTP relay без аутентификации разрешён, а выключенный SMS не требует ключа.</summary>
    [Fact]
    public void RealEmailDoesNotRequireDisabledSms()
    {
        var values = CreateSettings();
        values["Delivery:LogOnly"] = "false";
        values["Sms:Enabled"] = "false";
        values.Remove("Sms:Provider");
        values.Remove("SmsRu:BaseUrl");
        values.Remove("SmsRu:RequestTimeoutSeconds");
        values["Smtp:Host"] = "smtp.example.com";
        values["Smtp:FromAddress"] = "contact@example.invalid";

        Validate(values);
    }

    /// <summary>Включённый реальный канал требует своих параметров до первой отправки.</summary>
    [Theory]
    [InlineData("Smtp:Host")]
    [InlineData("Smtp:FromAddress")]
    [InlineData("Smtp:Password")]
    [InlineData("SmsRu:ApiKey")]
    [InlineData("Telegram:BotToken")]
    [InlineData("Telegram:OwnerChatId")]
    public void RealDeliveryRequiresProviderSettings(string key)
    {
        var values = CreateRealSettings();
        values.Remove(key);

        Assert.Throws<InvalidOperationException>(() => Validate(values));
    }

    /// <summary>Некорректные значения выявляются до запуска транспорта и HTTP-клиента.</summary>
    [Theory]
    [InlineData("Sms:Provider", "unregistered")]
    [InlineData("Email:Provider", "unregistered")]
    [InlineData("Delivery:LogOnly", "")]
    [InlineData("Delivery:LogOnly", "incorrect")]
    [InlineData("RabbitMq:Port", "0")]
    [InlineData("Smtp:Port", "65536")]
    [InlineData("SmsRu:BaseUrl", "relative/path")]
    [InlineData("SmsRu:BaseUrl", "file:///tmp/provider")]
    [InlineData("SmsRu:RequestTimeoutSeconds", "0")]
    [InlineData("SmsRu:ApiKey", "replace-with-key")]
    [InlineData("Telegram:Provider", "unregistered")]
    [InlineData("Telegram:RequestTimeoutSeconds", "0")]
    [InlineData("Telegram:BotToken", "replace-with-token")]
    [InlineData("Telegram:OwnerChatId", "chat")]
    [InlineData("TelegramClient:Provider", "unregistered")]
    [InlineData("TelegramClient:CredentialBaseUrl", "http://opencrm.example.com")]
    [InlineData("TelegramClient:RelayUrl", "https://relay.example.com/sendMessage")]
    [InlineData("TelegramClient:RequestTimeoutSeconds", "0")]
    [InlineData("TelegramClient:CredentialKey", "short-key")]
    [InlineData("TelegramClient:RelayKey", "short-key")]
    [InlineData("Max:Provider", "unregistered")]
    [InlineData("Max:CredentialBaseUrl", "http://opencrm.example.com")]
    [InlineData("Max:CredentialBaseUrl", "relative/path")]
    [InlineData("Max:RequestTimeoutSeconds", "0")]
    [InlineData("Max:CredentialKey", "replace-with-key")]
    [InlineData("Max:CredentialKey", "short-key")]
    public void InvalidSettingFailsStartup(string key, string value)
    {
        var values = CreateRealSettings();
        if (key.StartsWith("TelegramClient:", StringComparison.Ordinal))
        {
            values["TelegramClient:Enabled"] = "true";
            values["TelegramClient:Provider"] = "telegram";
            values["TelegramClient:CredentialBaseUrl"] = "https://opencrm.example.com";
            values["TelegramClient:CredentialKey"] = "test-notification-service-key-123456789";
            values["TelegramClient:RelayUrl"] = "https://relay.example.com/sendOrganizationMessage";
            values["TelegramClient:RelayKey"] = "test-relay-service-key-123456789";
            values["TelegramClient:RequestTimeoutSeconds"] = "15";
        }

        values[key] = value;

        Assert.Throws<InvalidOperationException>(() => Validate(values));
    }

    /// <summary>При использовании шлюза токен и чат хранятся в Cloudflare, а ключ шлюза обязателен.</summary>
    [Fact]
    public void RelayRequiresKeyButNotTelegramCredentials()
    {
        var values = CreateRealSettings();
        values["Telegram:RelayUrl"] = "https://relay.example.com/sendMessage";
        values["Telegram:RelayKey"] = "relay-secret";
        values.Remove("Telegram:BotToken");
        values.Remove("Telegram:OwnerChatId");

        Validate(values);

        values.Remove("Telegram:RelayKey");
        Assert.Throws<InvalidOperationException>(() => Validate(values));
    }

    /// <summary>Адрес шлюза должен использовать HTTPS и не содержать секретов.</summary>
    [Theory]
    [InlineData("http://relay.example.com/sendMessage")]
    [InlineData("https://user:contact@example.invalid/sendMessage")]
    [InlineData("https://relay.example.com/sendMessage?key=secret")]
    [InlineData("relative/path")]
    public void InvalidRelayUrlFailsStartup(string relayUrl)
    {
        var values = CreateRealSettings();
        values["Telegram:RelayUrl"] = relayUrl;
        values["Telegram:RelayKey"] = "relay-secret";

        Assert.Throws<InvalidOperationException>(() => Validate(values));
    }

    /// <summary> Валидирует переданный набор настроек конфигурации. </summary>
    private static void Validate(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var environment = Mock.Of<IHostEnvironment>(item => item.EnvironmentName == Environments.Development);
        StartupConfigurationValidator.Validate(configuration, environment);
    }

    /// <summary> Формирует минимальный корректный набор настроек. </summary>
    private static Dictionary<string, string?> CreateSettings()
    {
        return new Dictionary<string, string?>
        {
            ["ConnectionStrings:NotificationsDb"] = "Host=localhost;Database=test",
            ["RabbitMq:Host"] = "localhost",
            ["RabbitMq:Port"] = "5672",
            ["RabbitMq:VirtualHost"] = "/",
            ["RabbitMq:Username"] = "test",
            ["RabbitMq:Password"] = "test",
            ["Delivery:LogOnly"] = "true",
            ["Email:Enabled"] = "true",
            ["Email:Provider"] = "smtp",
            ["Smtp:Port"] = "587",
            ["Smtp:UseSsl"] = "false",
            ["Sms:Enabled"] = "true",
            ["Sms:Provider"] = "smsru",
            ["SmsRu:BaseUrl"] = "https://sms.ru",
            ["SmsRu:RequestTimeoutSeconds"] = "15",
            ["Telegram:Enabled"] = "false",
            ["TelegramClient:Enabled"] = "false",
            ["TelegramClient:Provider"] = "telegram",
            ["TelegramClient:RequestTimeoutSeconds"] = "15",
            ["Max:Enabled"] = "false"
        };
    }

    /// <summary> Формирует набор настроек, приближенный к боевому. </summary>
    private static Dictionary<string, string?> CreateRealSettings()
    {
        var values = CreateSettings();
        values["Delivery:LogOnly"] = "false";
        values["Smtp:Host"] = "smtp.example.com";
        values["Smtp:FromAddress"] = "contact@example.invalid";
        values["Smtp:Username"] = "sender";
        values["Smtp:Password"] = "test-password";
        values["SmsRu:ApiKey"] = "test-api-key";
        values["Telegram:Enabled"] = "true";
        values["Telegram:Provider"] = "telegram";
        values["Telegram:RequestTimeoutSeconds"] = "15";
        values["Telegram:BotToken"] = "test-bot-token";
        values["Telegram:OwnerChatId"] = "123456789";
        values["Max:Enabled"] = "true";
        values["Max:Provider"] = "max";
        values["Max:RequestTimeoutSeconds"] = "15";
        values["Max:CredentialBaseUrl"] = "https://opencrm.example.com";
        values["Max:CredentialKey"] = "test-notification-service-key-123456789";

        return values;
    }
}
