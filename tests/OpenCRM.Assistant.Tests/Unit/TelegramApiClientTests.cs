using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text;
using System.Text.Json;
using Moq;
using OpenCRM.WebApp.Infrastructure.Assistant.Telegram;

namespace OpenCRM.Assistant.Tests.Unit;

public sealed class TelegramApiClientTests
{
    [Fact]
    public async Task GetUpdatesAsync_FirstRequestOmitsOffset()
    {
        using var handler = new PollingHandler(["""{"ok":true,"result":[]}"""]);
        var client = CreatePollingClient(handler);

        Assert.Empty(await client.GetUpdatesAsync(null, CancellationToken.None));
        using var request = JsonDocument.Parse(Assert.Single(handler.Payloads));

        Assert.False(request.RootElement.TryGetProperty("offset", out _));
    }

    [Fact]
    public async Task SendMessageAsync_TokenColonDoesNotChangeHttpsHost()
    {
        using var handler = new PollingHandler(["""{"ok":true,"result":{}}"""]);
        var client = CreatePollingClient(handler);

        await client.SendMessageAsync(123, "Проверка", null, CancellationToken.None);
        var uri = Assert.Single(handler.Uris);

        Assert.Equal("https", uri.Scheme);
        Assert.Equal("api.telegram.org", uri.Host);
        Assert.Equal("/bot123456:test-token/sendMessage", uri.AbsolutePath);
    }

    [Fact]
    public async Task GetUpdatesAsync_UsesOffsetAndConfiguredLimits()
    {
        using var handler = new PollingHandler(["""{"ok":true,"result":[{"update_id":42}]}"""]);
        var client = CreatePollingClient(handler);

        var updates = await client.GetUpdatesAsync(42, CancellationToken.None);
        using var request = JsonDocument.Parse(Assert.Single(handler.Payloads));

        Assert.Equal(42, Assert.Single(updates).GetProperty("update_id").GetInt64());
        Assert.Equal(42, request.RootElement.GetProperty("offset").GetInt64());
        Assert.Equal(20, request.RootElement.GetProperty("timeout").GetInt32());
        Assert.Equal(20, request.RootElement.GetProperty("limit").GetInt32());
        Assert.Equal(new[] { "message", "callback_query" }, request.RootElement.GetProperty("allowed_updates").EnumerateArray().Select(item => item.GetString()));
        Assert.All(handler.Uris, uri =>
        {
            Assert.Equal("https", uri.Scheme);
            Assert.Equal("api.telegram.org", uri.Host);
            Assert.StartsWith("/bot123456:test-token/", uri.AbsolutePath);
        });
    }

    [Fact]
    public async Task ValidatePollingBotAsync_RejectsAnotherBotBeforeReadingUpdates()
    {
        using var handler = new PollingHandler(["""{"ok":true,"result":{"username":"another_bot"}}"""]);
        var client = CreatePollingClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ValidatePollingBotAsync(CancellationToken.None));

        Assert.Equal(new[] { "getMe" }, handler.Methods);
    }

    [Fact]
    public async Task ValidatePollingBotAsync_DoesNotDeleteAnExistingWebhook()
    {
        using var handler = new PollingHandler([
            """{"ok":true,"result":{"username":"test_pilot_bot"}}""",
            """{"ok":true,"result":{"url":"https://example.com/webhook"}}"""]);
        var client = CreatePollingClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ValidatePollingBotAsync(CancellationToken.None));

        Assert.Equal(new[] { "getMe", "getWebhookInfo" }, handler.Methods);
    }

    [Fact]
    public async Task GetUpdatesAsync_RejectsOversizedResponse()
    {
        using var handler = new PollingHandler([new string('a', 100)]) { DeclaredLength = 2_000_000 };
        var client = CreatePollingClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetUpdatesAsync(null, CancellationToken.None));

        Assert.Contains("размер", exception.Message);
    }

    [Fact]
    public async Task GetUpdatesAsync_DoesNotExposeTelegramErrorOrToken()
    {
        using var handler = new PollingHandler(["""{"ok":false,"description":"test-token secret details"}"""]);
        var client = CreatePollingClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetUpdatesAsync(null, CancellationToken.None));

        Assert.DoesNotContain("test-token", exception.Message);
        Assert.DoesNotContain("secret details", exception.Message);
    }

    [Fact]
    public async Task SendMessageAsync_LeavesTransportClosedWhenNoBotTokenIsConfigured()
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var client = new AssistantTelegramApiClient(factory.Object, new ConfigurationBuilder().Build());

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendMessageAsync(123, "test", null, CancellationToken.None));

        factory.Verify(factory => factory.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DownloadVoiceAsync_RejectsInvalidLimitBeforeCallingTelegram()
    {
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AssistantTelegram:BotToken"] = "test-token"
        }).Build();
        var client = new AssistantTelegramApiClient(factory.Object, configuration);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadVoiceAsync("file-id", 0, CancellationToken.None));

        factory.Verify(factory => factory.CreateClient(It.IsAny<string>()), Times.Never);
    }
    private static AssistantTelegramApiClient CreatePollingClient(PollingHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "assistant-appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AssistantTelegram:BotToken"] = "123456:test-token",
                ["AssistantTelegram:BotUsername"] = "test_pilot_bot"
            }).Build();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient("assistant-telegram")).Returns(() => new HttpClient(handler, disposeHandler: false));

        return new AssistantTelegramApiClient(factory.Object, configuration);
    }

    private sealed class PollingHandler(IEnumerable<string> responses) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responses);

        public List<string> Methods { get; } = [];

        public List<string> Payloads { get; } = [];

        public List<Uri> Uris { get; } = [];

        public long? DeclaredLength { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Methods.Add(request.RequestUri!.Segments[^1]);
            this.Uris.Add(request.RequestUri);
            this.Payloads.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(this._responses.Dequeue(), Encoding.UTF8, "application/json")
            };
            if (this.DeclaredLength.HasValue)
            {
                response.Content.Headers.ContentLength = this.DeclaredLength;
            }

            return response;
        }
    }
}
