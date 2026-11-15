using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Notifications.Application;
using Notifications.Contracts;
using Notifications.Domain;
using Notifications.Infrastructure;
using Notifications.Infrastructure.Providers.Telegram;

namespace Notifications.Tests.Unit;

/// <summary>Проверяет безопасную адресацию и подтверждение доставки Telegram.</summary>
public sealed class TelegramNotificationTests
{
    /// <summary>Возвращает идентификатор только после подтверждения Bot API.</summary>
    [Fact]
    public async Task AcceptedTelegramMessageReturnsMessageId()
    {
        var handler = new TelegramResponseHandler("""{"ok":true,"result":{"message_id":734}}""");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org") };
        var sender = CreateSender(client);

        var messageId = await sender.SendAsync("owner", "Заголовок", "Текст", false, CancellationToken.None);

        Assert.Equal("734", messageId);
        Assert.Equal("/bot123456:secret-token/sendMessage", handler.RequestUri!.AbsolutePath);
        using var content = JsonDocument.Parse(handler.Content!);
        Assert.Equal("123456789", content.RootElement.GetProperty("chat_id").GetString());
        Assert.Equal("Заголовок\n\nТекст", content.RootElement.GetProperty("text").GetString());
    }

    /// <summary>Шлюз получает только текст и отдельный ключ, без токена и ID чата Telegram.</summary>
    [Fact]
    public async Task RelayMessageDoesNotExposeTelegramCredentials()
    {
        var handler = new TelegramResponseHandler("""{"ok":true,"result":{"message_id":735}}""");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org") };
        var sender = CreateSender(client, new TelegramOptions
        {
            RelayUrl = "https://relay.example.com/sendMessage",
            RelayKey = "relay-secret"
        });

        var messageId = await sender.SendAsync("owner", null, "Текст", false, CancellationToken.None);

        Assert.Equal("735", messageId);
        Assert.Equal("https://relay.example.com/sendMessage", handler.RequestUri!.ToString());
        Assert.Equal("Bearer relay-secret", handler.Authorization);
        using var content = JsonDocument.Parse(handler.Content!);
        Assert.Equal("Текст", content.RootElement.GetProperty("text").GetString());
        Assert.False(content.RootElement.TryGetProperty("chat_id", out _));
    }

    /// <summary>Ответ без успешного результата и HTTP-ошибки не считаются доставкой.</summary>
    [Theory]
    [InlineData("""{"ok":false,"description":"error"}""")]
    [InlineData("""{"ok":true,"result":{}}""")]
    [InlineData("not-json")]
    public async Task TelegramRejectionIsNotSuccess(string response)
    {
        using var client = new HttpClient(new TelegramResponseHandler(response))
        {
            BaseAddress = new Uri("https://api.telegram.org")
        };
        var sender = CreateSender(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync("owner", null, "Текст", false, CancellationToken.None));

        Assert.DoesNotContain("secret-token", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>HTTP-ошибка провайдера не считается успешной отправкой.</summary>
    [Fact]
    public async Task HttpRejectionIsNotSuccess()
    {
        using var client = new HttpClient(new TelegramResponseHandler(
            """{"ok":true,"result":{"message_id":734}}""",
            HttpStatusCode.InternalServerError))
        {
            BaseAddress = new Uri("https://api.telegram.org")
        };
        var sender = CreateSender(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync("owner", null, "Текст", false, CancellationToken.None));
    }

    /// <summary>При сетевой ошибке токен из URL не попадает в исключение отправителя.</summary>
    [Fact]
    public async Task HttpFailureDoesNotExposeBotToken()
    {
        using var client = new HttpClient(new FailingTelegramHandler())
        {
            BaseAddress = new Uri("https://api.telegram.org")
        };
        var sender = CreateSender(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync("owner", null, "Текст", false, CancellationToken.None));

        Assert.DoesNotContain("secret-token", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Канал не позволяет указать посторонний chat_id вместо логического получателя.</summary>
    [Fact]
    public async Task RecipientMustBeOwnerAlias()
    {
        var handler = new TelegramResponseHandler("""{"ok":true,"result":{"message_id":734}}""");
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org") };
        var sender = CreateSender(client);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sender.SendAsync("-100123456", null, "Текст", false, CancellationToken.None));

        Assert.Null(handler.RequestUri);
    }

    /// <summary>Клиентский Telegram отдельно разрешает токен своей организации и шлёт через защищённый relay.</summary>
    [Fact]
    public async Task OrganizationTelegramResolvesTokenAndSendsThroughRelay()
    {
        var handler = new OrganizationTelegramResponseHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org") };
        var options = new TelegramOptions
        {
            CredentialBaseUrl = "https://opencrm.example.com",
            CredentialKey = "service-key",
            RelayUrl = "https://relay.example.com/sendOrganizationMessage",
            RelayKey = "relay-secret"
        };
        var sender = new TelegramNotificationSender(
            client,
            new DeliveryOptions { LogOnly = false },
            options,
            NullLogger<TelegramNotificationSender>.Instance,
            NotificationChannel.TelegramClient);

        var messageId = await sender.SendAsync(
            "7ac4ed0f408f4d0586cee9e9f0867810:123456789",
            "Запись",
            "Подтверждена",
            false,
            CancellationToken.None,
            idempotencyKey: "telegram-client:notification-1");

        Assert.Equal("736", messageId);
        Assert.Equal(
            "https://opencrm.example.com/api/internal/notification-bots/telegram/7ac4ed0f408f4d0586cee9e9f0867810",
            handler.CredentialRequestUri);
        Assert.Equal("service-key", handler.NotificationServiceKey);
        Assert.Equal("telegram-client:notification-1", handler.NotificationKey);
        Assert.Equal("123456789", handler.NotificationRecipient);
        Assert.Equal("https://relay.example.com/sendOrganizationMessage", handler.RelayRequestUri);
        Assert.Equal("Bearer relay-secret", handler.RelayAuthorization);
        using var content = JsonDocument.Parse(handler.RelayContent!);
        Assert.Equal("organization-bot-token", content.RootElement.GetProperty("bot_token").GetString());
        Assert.Equal("123456789", content.RootElement.GetProperty("chat_id").GetString());
        Assert.Equal("Запись\n\nПодтверждена", content.RootElement.GetProperty("text").GetString());
    }

    /// <summary>Клиентский канал не принимает адрес владельца и требует ключ уведомления.</summary>
    [Theory]
    [InlineData("owner")]
    [InlineData("7ac4ed0f408f4d0586cee9e9f0867810:abc")]
    [InlineData("organization:123456789")]
    public async Task OrganizationTelegramRejectsInvalidRecipients(string recipient)
    {
        var handler = new OrganizationTelegramResponseHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org") };
        var sender = new TelegramNotificationSender(
            client,
            new DeliveryOptions { LogOnly = false },
            new TelegramOptions(),
            NullLogger<TelegramNotificationSender>.Instance,
            NotificationChannel.TelegramClient);

        await Assert.ThrowsAsync<ArgumentException>(() => sender.SendAsync(
            recipient, null, "Текст", false, CancellationToken.None));

        Assert.Null(handler.CredentialRequestUri);
        Assert.Null(handler.RelayRequestUri);
    }

    /// <summary>Строковый контракт Telegram преобразуется в доменный канал.</summary>
    [Fact]
    public void TelegramChannelMapsFromContract()
    {
        Assert.Equal(NotificationChannel.Telegram, NotificationMapping.ParseChannel(NotificationChannels.Telegram));
        Assert.Equal(
            NotificationChannel.TelegramClient,
            NotificationMapping.ParseChannel(NotificationChannels.TelegramClient));
    }

    /// <summary> Создаёт отправитель Telegram-уведомлений с переданным HttpClient и настройками. </summary>
    private static TelegramNotificationSender CreateSender(HttpClient client, TelegramOptions? options = null)
    {
        return new TelegramNotificationSender(
            client,
            new DeliveryOptions { LogOnly = false },
            options ?? new TelegramOptions
            {
                BotToken = "123456:secret-token",
                OwnerChatId = "123456789"
            },
            NullLogger<TelegramNotificationSender>.Instance,
            NotificationChannel.Telegram);
    }

    /// <summary> Тестовый обработчик HTTP, возвращающий фиксированное тело и статус Telegram. </summary>
    private sealed class TelegramResponseHandler(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        /// <summary> URI последнего зафиксированного запроса, если был. </summary>
        public Uri? RequestUri { get; private set; }

        /// <summary> Тело последнего зафиксированного запроса, если было. </summary>
        public string? Content { get; private set; }

        /// <summary> Заголовок авторизации последнего запроса, если был. </summary>
        public string? Authorization { get; private set; }

        /// <summary> Записывает параметры запроса и возвращает настроенный ответ. </summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Content = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            };
        }
    }

    /// <summary> Тестовый обработчик HTTP, имитирующий ошибку отправки в Telegram. </summary>
    private sealed class FailingTelegramHandler : HttpMessageHandler
    {
        /// <summary> Возвращает ответ с ошибкой на любой запрос. </summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException($"Request failed for {request.RequestUri}.");
        }
    }

    /// <summary> Тестовый обработчик HTTP, эмулирующий получение учетных данных организации и релей Telegram. </summary>
    private sealed class OrganizationTelegramResponseHandler : HttpMessageHandler
    {
        /// <summary> URI запроса учетных данных организации, если был. </summary>
        public string? CredentialRequestUri { get; private set; }

        /// <summary> Ключ сервиса уведомлений из запроса учетных данных, если был. </summary>
        public string? NotificationServiceKey { get; private set; }

        /// <summary> Ключ уведомления из запроса учетных данных, если был. </summary>
        public string? NotificationKey { get; private set; }

        /// <summary> Получатель уведомления из запроса учетных данных, если был. </summary>
        public string? NotificationRecipient { get; private set; }

        /// <summary> URI запроса к релею Telegram, если был. </summary>
        public string? RelayRequestUri { get; private set; }

        /// <summary> Заголовок авторизации запроса к релею, если был. </summary>
        public string? RelayAuthorization { get; private set; }

        /// <summary> Тело запроса к релею, если было. </summary>
        public string? RelayContent { get; private set; }

        /// <summary> Обрабатывает запросы учетных данных и релея, возвращая настроенные ответы. </summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                CredentialRequestUri = request.RequestUri!.ToString();
                NotificationServiceKey = request.Headers.GetValues("X-Notification-Service-Key").Single();
                NotificationKey = request.Headers.GetValues("X-Notification-Key").Single();
                NotificationRecipient = request.Headers.GetValues("X-Notification-Recipient").Single();

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"botToken\":\"organization-bot-token\"}")
                };
            }

            RelayRequestUri = request.RequestUri!.ToString();
            RelayAuthorization = request.Headers.Authorization?.ToString();
            RelayContent = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":736}}")
            };
        }
    }
}
