using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notifications.Contracts;
using Notifications.Infrastructure;

namespace Notifications.Tests.Integration;

/// <summary>Проверяет безопасное повторение адресной доставки после явного отказа провайдера.</summary>
[Trait("Category", "Integration")]
public sealed class ConversationDeliveryRetryTests(MessagingFixture fixture) : IClassFixture<MessagingFixture>
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(ConversationDeliveryProvider.Telegram)]
    [InlineData(ConversationDeliveryProvider.Max)]
    public async Task SafeProviderFailureKeepsInboxPendingAndRetriesWithANewLease(
        ConversationDeliveryProvider providerCode)
    {
        // Проверка управляет арендой вручную; фоновый обработчик не должен забирать её запись.
        var inboxWorker = fixture.Services.GetServices<IHostedService>()
            .OfType<ConversationDeliveryInboxWorker>()
            .Single();

        await inboxWorker.StopAsync(CancellationToken.None);

        var messageId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var firstLeaseId = Guid.NewGuid();
        var secondLeaseId = Guid.NewGuid();
        var providerCallCount = 0;
        var completedResults = new ConcurrentQueue<ConversationDeliveryResult>();
        var coreHandler = new StubHttpMessageHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/claim", StringComparison.Ordinal))
            {
                var leaseId = completedResults.IsEmpty ? firstLeaseId : secondLeaseId;
                var claim = CreateDeliveryClaim(providerCode, messageId, requestId, leaseId);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(claim)
                };
            }

            using var completeRequest = await JsonDocument.ParseAsync(await request.Content!.ReadAsStreamAsync());
            var result = JsonSerializer.Deserialize<ConversationDeliveryCompleteRequest>(
                completeRequest.RootElement.GetRawText(),
                WebJsonOptions);
            Assert.NotNull(result);
            completedResults.Enqueue(result.Result);

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var providerHandler = new StubHttpMessageHandler(_ =>
        {
            var call = Interlocked.Increment(ref providerCallCount);
            if (call == 1)
            {
                var response = new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent(providerCode == ConversationDeliveryProvider.Telegram
                        ? "{\"parameters\":{\"retry_after\":1}}"
                        : "{}")
                };
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    TimeSpan.FromSeconds(1));

                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(providerCode == ConversationDeliveryProvider.Telegram
                    ? "{\"ok\":true,\"result\":{\"message_id\":735}}"
                    : "{\"message\":{\"body\":{\"mid\":\"accepted-735\"}}}")
            });
        });
        var clients = new RetryTestHttpClientFactory(coreHandler, providerCode, providerHandler);
        var timeProvider = new RetryTimeProvider(TimeProvider.System.GetUtcNow());
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var inboxStore = new ConversationDeliveryInboxStore(db, timeProvider);
        await inboxStore.EnqueueAsync(providerCode, messageId, requestId, CancellationToken.None);
        var processor = CreateProcessor(providerCode, clients, scope.ServiceProvider, timeProvider);

        var firstInboxMessage = Assert.Single(await inboxStore.ClaimDueAsync(
            providerCode,
            1,
            TimeSpan.FromMinutes(5),
            CancellationToken.None));
        await Assert.ThrowsAsync<ConversationDeliveryDeferredException>(() => processor.ProcessAsync(
            new ConversationDeliveryWakeup(providerCode, messageId, requestId),
            CancellationToken.None));
        var retryAt = timeProvider.GetUtcNow().AddSeconds(1);
        await inboxStore.CompleteAsync(
            firstInboxMessage,
            firstInboxMessage.LeaseId!.Value,
            retryAt,
            "delivery-deferred",
            CancellationToken.None);

        Assert.Equal("pending", firstInboxMessage.Status);
        Assert.Single(completedResults, result => result.Status == "failed" && result.SafeToRetry);
        timeProvider.UtcNow = retryAt;
        using var retryScope = fixture.Services.CreateScope();
        var retryDb = retryScope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var retryInboxStore = new ConversationDeliveryInboxStore(retryDb, timeProvider);
        var secondInboxMessage = Assert.Single(await retryInboxStore.ClaimDueAsync(
            providerCode,
            1,
            TimeSpan.FromMinutes(5),
            CancellationToken.None));
        Assert.NotEqual(firstInboxMessage.LeaseId, secondInboxMessage.LeaseId);
        var retryProcessor = CreateProcessor(providerCode, clients, retryScope.ServiceProvider, timeProvider);
        await retryProcessor.ProcessAsync(
            new ConversationDeliveryWakeup(providerCode, messageId, requestId),
            CancellationToken.None);
        await retryInboxStore.CompleteAsync(
            secondInboxMessage,
            secondInboxMessage.LeaseId!.Value,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(2, providerCallCount);
        Assert.Equal(2, completedResults.Count);
        Assert.Equal("accepted", completedResults.Last().Status);
        Assert.Equal("completed", await retryDb.ConversationDeliveryInbox
            .Where(item => item.MessageId == messageId && item.RequestId == requestId)
            .Select(item => item.Status)
            .SingleAsync());
    }

    private static IConversationDeliveryProcessor CreateProcessor(
        ConversationDeliveryProvider providerCode,
        IHttpClientFactory clients,
        IServiceProvider services,
        TimeProvider timeProvider)
    {
        var stateStore = new ConversationDeliveryAttemptStore(
            services.GetRequiredService<NotificationDbContext>());
        if (providerCode == ConversationDeliveryProvider.Telegram)
        {
            return new TelegramChatDeliveryProcessor(
                clients,
                stateStore,
                Options.Create(new TelegramChatOptions
                {
                    OpenCRMBaseUrl = "https://opencrm.example",
                    ServiceKey = new string('s', 40),
                    RelayBaseUrl = "https://relay.example",
                    RelayKey = new string('r', 40),
                    RequestTimeoutSeconds = 5
                }));
        }

        return new MaxChatDeliveryProcessor(
            clients,
            stateStore,
            new MaxConversationRateLimiter(services.GetRequiredService<NotificationDbContext>(), timeProvider),
            Options.Create(new MaxChatOptions
            {
                OpenCRMBaseUrl = "https://opencrm.example",
                ServiceKey = new string('s', 40),
                RequestTimeoutSeconds = 5
            }));
    }

    private static ConversationDeliveryClaimResponse CreateDeliveryClaim(
        ConversationDeliveryProvider providerCode,
        Guid messageId,
        Guid requestId,
        Guid leaseId)
    {
        return new ConversationDeliveryClaimResponse
        {
            MessageId = messageId,
            LeaseId = leaseId,
            OrganizationId = Guid.NewGuid(),
            ProviderCode = providerCode,
            ConnectionId = Guid.NewGuid(),
            ProviderBotId = "123",
            DeliveryAddress = "456",
            ExternalConversationId = "456",
            BotUsername ="test_bot",
            RequestId = requestId,
            Token = "12345:test_token_abcdefghijklmnopqrstuvwxyz",
            Text = "Проверка повтора"
        };
    }

    private sealed class RetryTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => this.UtcNow;
    }

    private sealed class RetryTestHttpClientFactory(
        HttpMessageHandler coreHandler,
        ConversationDeliveryProvider providerCode,
        HttpMessageHandler providerHandler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var handler = name switch
            {
                "TelegramChatOpenCRM" or "MaxChatOpenCRM" => coreHandler,
                "TelegramChatRelay" when providerCode == ConversationDeliveryProvider.Telegram => providerHandler,
                "MaxConversationApi" when providerCode == ConversationDeliveryProvider.Max => providerHandler,
                _ => throw new InvalidOperationException($"Unexpected HTTP client: {name}.")
            };
            var client = new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = name switch
                {
                    "TelegramChatRelay" => new Uri("https://relay.example/"),
                    "MaxConversationApi" => new Uri("https://platform-api2.max.ru/"),
                    _ => new Uri("https://opencrm.example/")
                }
            };

            return client;
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }
}
