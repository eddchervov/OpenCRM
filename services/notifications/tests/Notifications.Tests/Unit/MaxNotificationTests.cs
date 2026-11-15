using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Notifications.Application;
using Notifications.Contracts;
using Notifications.Domain;
using Notifications.Infrastructure;
using Notifications.Infrastructure.Providers.Max;

namespace Notifications.Tests.Unit;

/// <summary>Проверяет безопасное разрешение токена организации и доставку сообщений в MAX.</summary>
public sealed class MaxNotificationTests
{
    /// <summary> Идентификатор организации, используемый в тестах MAX. </summary>
    private static readonly Guid s_organizationId = Guid.Parse("7ac4ed0f-408f-4d05-86ce-e9e9f0867810");

    /// <summary>Отправитель запрашивает токен нужной организации и считает принятием только HTTP 200.</summary>
    [Fact]
    public async Task AcceptedMessageResolvesOrganizationTokenAndPostsToUser()
    {
        var handler = new MaxResponseHandler();
        using var client = new HttpClient(handler);
        var sender = CreateSender(client);

        var messageId = await sender.SendAsync(
            $"{s_organizationId:N}:123456789", "Заголовок", "Текст", false, CancellationToken.None,
            idempotencyKey: "max:test:1");

        Assert.Equal("mid.123", messageId);
        Assert.Equal(
            $"https://opencrm.example.com/api/internal/notification-bots/max/{s_organizationId:N}",
            handler.Requests[0].Uri);
        Assert.Equal("service-key", handler.Requests[0].NotificationServiceKey);
        Assert.Equal("max:test:1", handler.Requests[0].NotificationKey);
        Assert.Equal("GET", handler.Requests[0].Method);
        Assert.Equal("https://platform-api2.max.ru/messages?user_id=123456789", handler.Requests[1].Uri);
        Assert.Equal("organization-bot-token", handler.Requests[1].Authorization);
        using var content = JsonDocument.Parse(handler.Requests[1].Content!);
        Assert.Equal("Заголовок\n\nТекст", content.RootElement.GetProperty("text").GetString());
    }

    /// <summary>Ответ MAX с любым статусом, кроме HTTP 200, не считается принятием.</summary>
    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task NonOkProviderResponseIsNotAccepted(HttpStatusCode statusCode)
    {
        using var client = new HttpClient(new MaxResponseHandler(providerStatusCode: statusCode));
        var sender = CreateSender(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            $"{s_organizationId:N}:123456789", null, "Текст", false, CancellationToken.None,
            idempotencyKey: "max:test:2"));
    }

    /// <summary>HTTP 200 без ID сообщения не фиксируется как подтверждённая доставка.</summary>
    [Fact]
    public async Task MissingMessageIdIsNotAccepted()
    {
        using var client = new HttpClient(new MaxResponseHandler(providerBody: """{"message":{"body":{}}}"""));
        var sender = CreateSender(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            $"{s_organizationId:N}:123456789", null, "Текст", false, CancellationToken.None,
            idempotencyKey: "max:test:missing-id"));
    }

    /// <summary>Некорректный получатель отклоняется до обращения к credential service.</summary>
    [Theory]
    [InlineData("organization:123")]
    [InlineData("7ac4ed0f408f4d0586cee9e9f0867810:0")]
    [InlineData("7ac4ed0f408f4d0586cee9e9f0867810:not-a-user")]
    public async Task InvalidRecipientIsRejected(string recipient)
    {
        var handler = new MaxResponseHandler();
        using var client = new HttpClient(handler);
        var sender = CreateSender(client);

        await Assert.ThrowsAsync<ArgumentException>(() => sender.SendAsync(
            recipient, null, "Текст", false, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    /// <summary>Отказ credential service не раскрывает переданный ключ или тело ответа.</summary>
    [Fact]
    public async Task CredentialServiceRejectionDoesNotExposeSecrets()
    {
        using var client = new HttpClient(new MaxResponseHandler(credentialStatusCode: HttpStatusCode.Forbidden));
        var sender = CreateSender(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            $"{s_organizationId:N}:123456789", null, "Текст", false, CancellationToken.None,
            idempotencyKey: "max:test:3"));

        Assert.DoesNotContain("service-key", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("organization-bot-token", exception.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Строковый контракт MAX преобразуется в доменный канал.</summary>
    [Fact]
    public void MaxChannelMapsFromContract()
    {
        Assert.Equal(NotificationChannel.Max, NotificationMapping.ParseChannel(NotificationChannels.Max));
    }

    /// <summary> Создаёт отправитель уведомлений MAX с переданным HttpClient. </summary>
    private static MaxNotificationSender CreateSender(HttpClient client)
    {
        return new MaxNotificationSender(
            client,
            new NoOpMaxConversationRateLimiter(),
            Options.Create(new DeliveryOptions { LogOnly = false }),
            Options.Create(new MaxOptions
            {
                CredentialBaseUrl = "https://opencrm.example.com",
                CredentialKey = "service-key",
                RequestTimeoutSeconds = 15
            }),
            NullLogger<MaxNotificationSender>.Instance);
    }

    private sealed class NoOpMaxConversationRateLimiter : IMaxConversationRateLimiter
    {
        public Task<IAsyncDisposable> AcquireConversationAsync(string key, CancellationToken cancellationToken)
        {
            return Task.FromResult<IAsyncDisposable>(NoOpLease.Instance);
        }

        public Task WaitForGlobalRequestSlotAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private sealed class NoOpLease : IAsyncDisposable
        {
            public static NoOpLease Instance { get; } = new();

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary> Тестовый обработчик HTTP с настраиваемыми статусами и телом для MAX. </summary>
    private sealed class MaxResponseHandler(
        HttpStatusCode credentialStatusCode = HttpStatusCode.OK,
        HttpStatusCode providerStatusCode = HttpStatusCode.OK,
        string providerBody = """{"message":{"body":{"mid":"mid.123"}}}""") : HttpMessageHandler
    {
        /// <summary> Список зафиксированных запросов к провайдеру. </summary>
        public List<RecordedRequest> Requests { get; } = [];

        /// <summary> Записывает запрос и возвращает настроенный ответ провайдера. </summary>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method.Method,
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues("X-Notification-Service-Key", out var serviceKeys)
                    ? serviceKeys.Single()
                    : null,
                request.Headers.TryGetValues("X-Notification-Key", out var notificationKeys)
                    ? notificationKeys.Single()
                    : null,
                request.Headers.TryGetValues("Authorization", out var authorizationValues)
                    ? authorizationValues.Single()
                    : null,
                request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(credentialStatusCode)
                {
                    Content = new StringContent("""{"botToken":"organization-bot-token"}""")
                };
            }

            return new HttpResponseMessage(providerStatusCode)
            {
                Content = new StringContent(providerBody)
            };
        }
    }

    /// <summary> Данные зафиксированного HTTP-запроса для проверок. </summary>
    private sealed record RecordedRequest(
        string Method,
        string Uri,
        string? NotificationServiceKey,
        string? NotificationKey,
        string? Authorization,
        string? Content);
}
