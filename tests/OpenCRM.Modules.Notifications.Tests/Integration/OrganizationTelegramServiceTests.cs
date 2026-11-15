using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;
using OpenCRM.Modules.Notifications.Telegram;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

public sealed class OrganizationTelegramServiceTests(ClientNotificationDatabaseFixture fixture)
    : IClassFixture<ClientNotificationDatabaseFixture>, IAsyncLifetime
{
    private const string OwnerId = "telegram-owner";
    private const string RelayKey = "relay-key-at-least-thirty-two-characters";

    public async Task InitializeAsync()
    {
        await using var context = fixture.CreateContext();
        await context.TelegramClientNotifications.ExecuteDeleteAsync();
        await context.OrganizationTelegramBots.ExecuteDeleteAsync();
        await fixture.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task OwnerCanReadSettingsAndNonOwnerCannot()
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")));

        var settings = JsonSerializer.SerializeToElement(await service.GetSettingsAsync(OwnerId, graph.OrganizationId));

        Assert.False(settings.GetProperty("isConfigured").GetBoolean());
        Assert.False(settings.GetProperty("isEnabled").GetBoolean());
        Assert.True(settings.GetProperty("isAvailable").GetBoolean());
        Assert.True(settings.GetProperty("notifyBooking").GetBoolean());
        Assert.True(settings.GetProperty("notifyReady").GetBoolean());
        Assert.Equal($"https://relay.example/api/updates/{graph.OrganizationId:D}", settings.GetProperty("webhookUrl").GetString());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetSettingsAsync("another-user", graph.OrganizationId));
    }

    [Fact]
    public async Task FreePlanCannotConnectTelegramBotButCanKeepReadingSettings()
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), hasClientMessagingFeature: false);

        var settings = JsonSerializer.SerializeToElement(await service.GetSettingsAsync(OwnerId, graph.OrganizationId));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
            OwnerId, graph.OrganizationId, "bot-token", true, true, true, CancellationToken.None));

        Assert.False(settings.GetProperty("isConfigured").GetBoolean());
        Assert.Contains("PRO", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await context.OrganizationTelegramBots.ToListAsync());
    }

    [Fact]
    public async Task SettingsReturnEmptyWebhookUrlWhenRelayBaseUrlIsMissing()
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = this.CreateService(
            context,
            new TestHttpMessageHandler(_ => this.Success("{}")),
            configuration: new Dictionary<string, string?> { ["TelegramOrganizationNotifications:RelayBaseUrl"] = null });

        var settings = JsonSerializer.SerializeToElement(await service.GetSettingsAsync(OwnerId, graph.OrganizationId));

        Assert.Equal(string.Empty, settings.GetProperty("webhookUrl").GetString());
    }

    [Fact]
    public async Task SettingsUseRelayAddressAndCanonicalOrganizationId()
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = this.CreateService(
            context,
            new TestHttpMessageHandler(_ => this.Success("{}")),
            configuration: new Dictionary<string, string?>
            {
                ["TelegramOrganizationNotifications:RelayBaseUrl"] = "https://bridge.example/tg/"
            });

        var settings = JsonSerializer.SerializeToElement(await service.GetSettingsAsync(OwnerId, graph.OrganizationId));

        Assert.Equal($"https://bridge.example/tg/updates/{graph.OrganizationId:D}",
            settings.GetProperty("webhookUrl").GetString());
    }

    [Fact]
    public async Task SettingsRejectRelayBaseUrlWithQueryString()
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = this.CreateService(
            context,
            new TestHttpMessageHandler(_ => this.Success("{}")),
            configuration: new Dictionary<string, string?>
            {
                ["TelegramOrganizationNotifications:RelayBaseUrl"] = "https://bridge.example/tg?token=secret"
            });

        var settings = JsonSerializer.SerializeToElement(await service.GetSettingsAsync(OwnerId, graph.OrganizationId));

        Assert.Equal(string.Empty, settings.GetProperty("webhookUrl").GetString());
    }

    [Fact]
    public async Task ClientAndMaintenanceStatusIncludeTheirCurrentNotifications()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, notification: true);
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationTelegramBots.SingleAsync();
        var client = await context.OrganizationClients.SingleAsync();
        var maintenanceId = await context.Maintenance
            .Where(maintenance => maintenance.Vehicle.OrganizationId == graph.OrganizationId)
            .Select(maintenance => maintenance.Id)
            .SingleAsync();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")));

        var clientStatus = JsonSerializer.SerializeToElement(await service.GetClientStatusAsync(
            OwnerId, graph.OrganizationId, graph.ClientId));
        var maintenanceNotifications = JsonSerializer.SerializeToElement(await service.GetMaintenanceNotificationsAsync(
            OwnerId, graph.OrganizationId, maintenanceId));

        Assert.True(clientStatus.GetProperty("isBotConnected").GetBoolean());
        Assert.True(clientStatus.GetProperty("isClientConnected").GetBoolean());
        Assert.Equal("test_bot", clientStatus.GetProperty("botUsername").GetString());
        Assert.Single(clientStatus.GetProperty("recentNotifications").EnumerateArray());
        Assert.Single(maintenanceNotifications.EnumerateArray());
        var unavailable = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")),
            configuration: new Dictionary<string, string?> { ["TelegramOrganizationNotifications:Enabled"] = "false" });
        var unavailableStatus = JsonSerializer.SerializeToElement(await unavailable.GetClientStatusAsync(
            OwnerId, graph.OrganizationId, graph.ClientId));
        Assert.False(unavailableStatus.GetProperty("isBotConnected").GetBoolean());
        bot.IsEnabled = false;
        await context.SaveChangesAsync();
        var disconnectedBotStatus = JsonSerializer.SerializeToElement(await service.GetClientStatusAsync(
            OwnerId, graph.OrganizationId, graph.ClientId));
        Assert.False(disconnectedBotStatus.GetProperty("isBotConnected").GetBoolean());
        bot.IsEnabled = true;
        client.TelegramNotificationsEnabled = false;
        await context.SaveChangesAsync();
        var disconnectedClientStatus = JsonSerializer.SerializeToElement(await service.GetClientStatusAsync(
            OwnerId, graph.OrganizationId, graph.ClientId));
        Assert.False(disconnectedClientStatus.GetProperty("isClientConnected").GetBoolean());
        client.TelegramNotificationsEnabled = true;
        client.TelegramChatId = null;
        await context.SaveChangesAsync();
        var missingChatStatus = JsonSerializer.SerializeToElement(await service.GetClientStatusAsync(
            OwnerId, graph.OrganizationId, graph.ClientId));
        Assert.False(missingChatStatus.GetProperty("isClientConnected").GetBoolean());
        context.OrganizationTelegramBots.Remove(bot);
        await context.SaveChangesAsync();
        var missingBotStatus = JsonSerializer.SerializeToElement(await service.GetClientStatusAsync(
            OwnerId, graph.OrganizationId, graph.ClientId));
        Assert.False(missingBotStatus.GetProperty("isBotConnected").GetBoolean());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetClientStatusAsync(
            OwnerId, graph.OrganizationId, Guid.NewGuid()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetMaintenanceNotificationsAsync(
            OwnerId, graph.OrganizationId, Guid.NewGuid()));
    }

    [Fact]
    public async Task OwnerCanSaveTelegramSettingsThroughTheRelayAndProtectTheStoredSecrets()
    {
        var graph = await this.CreateOrganizationAsync();
        var protector = new EphemeralDataProtectionProvider();
        var requests = new List<HttpRequestMessage>();
        using var handler = new TestHttpMessageHandler(request =>
        {
            requests.Add(CloneRequest(request));
            return request.RequestUri!.AbsolutePath.EndsWith("getOrganizationBot", StringComparison.Ordinal)
                ? this.Success("{\"ok\":true,\"result\":{\"id\":123456,\"is_bot\":true,\"username\":\"@Test_Bot\"}}")
                : this.Success("{\"ok\":true}");
        });
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, handler, protector);

        await service.SaveSettingsAsync(OwnerId, graph.OrganizationId, "  bot-token  ", true, false, true, CancellationToken.None);

        var bot = await context.OrganizationTelegramBots.SingleAsync();
        Assert.Equal("test_bot", bot.Username);
        Assert.True(bot.IsEnabled);
        Assert.False(bot.NotifyBooking);
        Assert.True(bot.NotifyReady);
        Assert.NotEqual("bot-token", bot.ProtectedToken);
        Assert.Equal("bot-token", protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Unprotect(bot.ProtectedToken));
        Assert.NotEqual("bot-token", bot.ProtectedWebhookSecret);
        var settings = JsonSerializer.SerializeToElement(await service.GetSettingsAsync(OwnerId, graph.OrganizationId));
        Assert.True(settings.GetProperty("isConfigured").GetBoolean());
        Assert.True(settings.GetProperty("isEnabled").GetBoolean());
        Assert.False(settings.GetProperty("notifyBooking").GetBoolean());
        Assert.True(settings.GetProperty("notifyReady").GetBoolean());
        Assert.Equal(2, requests.Count);
        Assert.Equal("Bearer", requests[0].Headers.Authorization!.Scheme);
        Assert.Equal(RelayKey, requests[0].Headers.Authorization!.Parameter);
        Assert.Equal("getOrganizationBot", requests[0].RequestUri!.AbsolutePath.TrimEnd('/').Split('/').Last());
        using var botRequest = JsonDocument.Parse(await requests[0].Content!.ReadAsStringAsync());
        Assert.Equal("bot-token", botRequest.RootElement.GetProperty("bot_token").GetString());
        using var webhookRequest = JsonDocument.Parse(await requests[1].Content!.ReadAsStringAsync());
        Assert.Equal(graph.OrganizationId.ToString("D"), webhookRequest.RootElement.GetProperty("organization_id").GetString());
        Assert.NotEqual("bot-token", webhookRequest.RootElement.GetProperty("secret").GetString());
        foreach (var request in requests)
        {
            request.Dispose();
        }
    }

    [Fact]
    public async Task EnablingRequiresFeatureAndValidRelaySettings()
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var disabledService = this.CreateService(
            context,
            new TestHttpMessageHandler(_ => throw new InvalidOperationException("Внешний запрос не ожидался.")),
            configuration: new Dictionary<string, string?> { ["TelegramOrganizationNotifications:Enabled"] = "false" });

        var disabled = await Assert.ThrowsAsync<InvalidOperationException>(() => disabledService.SaveSettingsAsync(
            OwnerId, graph.OrganizationId, "token", true, true, true, CancellationToken.None));
        Assert.Equal("Клиентский Telegram ещё не включён в opencrm.example.invalid.", disabled.Message);

        var invalidRelayService = this.CreateService(
            context,
            new TestHttpMessageHandler(_ => throw new InvalidOperationException("Внешний запрос не ожидался.")),
            configuration: new Dictionary<string, string?> { ["TelegramOrganizationNotifications:RelayKey"] = "short" });
        var invalidRelay = await Assert.ThrowsAsync<InvalidOperationException>(() => invalidRelayService.SaveSettingsAsync(
            OwnerId, graph.OrganizationId, "token", true, true, true, CancellationToken.None));
        Assert.Equal("Клиентский Telegram ещё не настроен в opencrm.example.invalid.", invalidRelay.Message);
    }

    [Fact]
    public async Task MissingTokenCannotCreateBotAndExistingBotCanBeDisabledWithoutNetworkCalls()
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("Внешний запрос не ожидался."));
        var service = this.CreateService(context, handler);

        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
            OwnerId, graph.OrganizationId, " ", true, true, true, CancellationToken.None));
        Assert.Equal("Укажите токен бота Telegram.", missing.Message);
        var protector = new EphemeralDataProtectionProvider();
        context.OrganizationTelegramBots.Add(this.CreateBot(graph.OrganizationId, protector, "webhook-secret"));
        await context.SaveChangesAsync();
        var serviceWithProtector = this.CreateService(context, handler, protector);
        await serviceWithProtector.SaveSettingsAsync(OwnerId, graph.OrganizationId, null, false, false, true, CancellationToken.None);

        var bot = await context.OrganizationTelegramBots.SingleAsync();
        Assert.False(bot.IsEnabled);
        Assert.False(bot.NotifyBooking);
        Assert.True(bot.NotifyReady);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData("http://relay.example/api")]
    [InlineData("not-a-url")]
    public async Task InvalidRelayCannotSaveBotSettings(string relayBaseUrl)
    {
        var graph = await this.CreateOrganizationAsync();
        using var handler = new TestHttpMessageHandler(request => request.RequestUri!.AbsolutePath.EndsWith("getOrganizationBot", StringComparison.Ordinal)
            ? this.Success("{\"ok\":true,\"result\":{\"id\":123456,\"is_bot\":true,\"username\":\"test_bot\"}}")
            : this.Success("{\"ok\":true}"));
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, handler, configuration: new Dictionary<string, string?>
        {
            ["TelegramOrganizationNotifications:RelayBaseUrl"] = relayBaseUrl
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
            OwnerId, graph.OrganizationId, "token", true, true, true, CancellationToken.None));

        Assert.Equal("Клиентский Telegram ещё не настроен в opencrm.example.invalid.", exception.Message);
        Assert.Equal(0, handler.RequestCount);
    }

    [Theory]
    [InlineData("{\"ok\":false}", "Telegram не вернул имя бота.")]
    [InlineData("{\"ok\":true}", "Telegram не вернул имя бота.")]
    public async Task RelayMustConfirmTheBotResponse(string response, string expectedMessage)
    {
        var graph = await this.CreateOrganizationAsync();
        using var handler = new TestHttpMessageHandler(_ => this.Success(response));
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
            OwnerId, graph.OrganizationId, "token", true, true, true, CancellationToken.None));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.False(await context.OrganizationTelegramBots.AnyAsync());
    }

    [Theory]
    [InlineData("http", "Telegram не подтвердил подключение бота. Проверьте токен и повторите попытку.")]
    [InlineData("cancel", "Telegram не подтвердил подключение бота. Проверьте токен и повторите попытку.")]
    [InlineData("json", "Telegram не подтвердил подключение бота. Проверьте токен и повторите попытку.")]
    [InlineData("status", "Telegram не принял токен бота.")]
    public async Task BotLookupFailureIsReportedWithoutPersistingSettings(string failure, string expectedMessage)
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        using var handler = new TestHttpMessageHandler(_ => failure switch
        {
            "http" => throw new HttpRequestException(),
            "cancel" => throw new TaskCanceledException(),
            "json" => this.Success("not-json"),
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
        });
        var service = this.CreateService(context, handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
            OwnerId, graph.OrganizationId, "token", true, true, true, CancellationToken.None));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.False(await context.OrganizationTelegramBots.AnyAsync());
    }

    [Theory]
    [InlineData("status", "Telegram не принял адрес вебхука. Проверьте доступность HTTPS-сайта.")]
    [InlineData("body", "Telegram не подтвердил подписку на события бота.")]
    [InlineData("missing", "Telegram не подтвердил подписку на события бота.")]
    public async Task RelayMustAcceptWebhookRegistration(string failure, string expectedMessage)
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        using var handler = new TestHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("getOrganizationBot", StringComparison.Ordinal))
            {
                return this.Success("{\"ok\":true,\"result\":{\"id\":123456,\"is_bot\":true,\"username\":\"test_bot\"}}");
            }

            return failure == "status"
                ? new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
                : this.Success(failure == "missing" ? "{}" : "{\"ok\":false}");
        });
        var service = this.CreateService(context, handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
            OwnerId, graph.OrganizationId, "token", true, true, true, CancellationToken.None));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.False(await context.OrganizationTelegramBots.AnyAsync());
    }

    [Fact]
    public async Task DuplicateBotUsernameIsRejectedWithoutSavingAnotherBot()
    {
        await this.CreateOrganizationAsync(includeBot: true, username: "same_bot");
        var secondGraph = await this.CreateOrganizationAsync("telegram-owner-2");
        using var handler = new TestHttpMessageHandler(request => request.RequestUri!.AbsolutePath.EndsWith("getOrganizationBot", StringComparison.Ordinal)
            ? this.Success("{\"ok\":true,\"result\":{\"id\":123456,\"is_bot\":true,\"username\":\"same_bot\"}}")
            : this.Success("{\"ok\":true}"));
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
            "telegram-owner-2", secondGraph.OrganizationId, "another-token", true, true, true, CancellationToken.None));

        Assert.Equal("Этот бот Telegram уже подключён к другой организации.", exception.Message);
        Assert.Equal(1, await context.OrganizationTelegramBots.CountAsync());
    }

    [Fact]
    public async Task ChangingBotUsernameDisconnectsPreviouslyLinkedClients()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, username: "old_bot");
        var protector = new EphemeralDataProtectionProvider();
        using var handler = new TestHttpMessageHandler(request => request.RequestUri!.AbsolutePath.EndsWith("getOrganizationBot", StringComparison.Ordinal)
            ? this.Success("{\"ok\":true,\"result\":{\"id\":123457,\"is_bot\":true,\"username\":\"new_bot\"}}")
            : this.Success("{\"ok\":true}"));
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationTelegramBots.SingleAsync();
        bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Protect("old-token");
        bot.ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Protect("old-secret");
        await context.SaveChangesAsync();
        var service = this.CreateService(context, handler, protector);

        await service.SaveSettingsAsync(OwnerId, graph.OrganizationId, "new-token", true, true, true, CancellationToken.None);

        var client = await context.OrganizationClients.SingleAsync();
        Assert.Null(client.TelegramChatId);
        Assert.Null(client.TelegramConnectedAt);
        Assert.False(client.TelegramNotificationsEnabled);
        Assert.Equal("new_bot", bot.Username);
    }

    [Fact]
    public async Task ExistingTelegramBotCanBeReenabledWithItsProtectedCredentials()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, username: "test_bot", botEnabled: false);
        var protector = new EphemeralDataProtectionProvider();
        var requests = new List<HttpRequestMessage>();
        using var handler = new TestHttpMessageHandler(request =>
        {
            requests.Add(CloneRequest(request));
            return request.RequestUri!.AbsolutePath.EndsWith("getOrganizationBot", StringComparison.Ordinal)
                ? this.Success("{\"ok\":true,\"result\":{\"id\":123456,\"is_bot\":true,\"username\":\"test_bot\"}}")
                : this.Success("{\"ok\":true}");
        });
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationTelegramBots.SingleAsync();
        var secret = "opencrm-webhook-secret";
        bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Protect("saved-token");
        bot.ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Protect(secret);
        await context.SaveChangesAsync();
        var service = this.CreateService(context, handler, protector);

        await service.SaveSettingsAsync(OwnerId, graph.OrganizationId, null, true, true, true, CancellationToken.None);

        Assert.True(bot.IsEnabled);
        Assert.Equal("saved-token", protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Unprotect(bot.ProtectedToken));
        using var webhook = JsonDocument.Parse(await requests[1].Content!.ReadAsStringAsync());
        Assert.Equal(secret, webhook.RootElement.GetProperty("secret").GetString());
        foreach (var request in requests)
        {
            request.Dispose();
        }
    }

    [Fact]
    public async Task LinkCreationRequiresConfiguredBotAndStoresOnlyHashedToken()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, botEnabled: true, username: "test_bot");
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")));

        var link = await service.CreateClientLinkAsync(OwnerId, graph.OrganizationId, graph.ClientId);

        Assert.StartsWith("https://t.me/test_bot?start=", link, StringComparison.Ordinal);
        var token = link[(link.IndexOf("?start=", StringComparison.Ordinal) + 7)..];
        var client = await context.OrganizationClients.SingleAsync();
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), client.TelegramLinkTokenHash);
        Assert.NotEqual(token, client.TelegramLinkTokenHash);
        Assert.InRange(client.TelegramLinkExpiresAt!.Value, DateTime.UtcNow.AddDays(6), DateTime.UtcNow.AddDays(8));
    }

    [Fact]
    public async Task ClientLinkReturnsErrorsForMissingBotOrClient()
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")));

        var missingBot = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateClientLinkAsync(OwnerId, graph.OrganizationId, graph.ClientId));
        Assert.Equal("Сначала подключите бота Telegram в настройках организации.", missingBot.Message);

        context.OrganizationTelegramBots.Add(new OrganizationTelegramBot
        {
            OrganizationId = graph.OrganizationId,
            Username ="test_bot",
            ProtectedToken = "protected-token",
            ProtectedWebhookSecret = "protected-secret",
            IsEnabled = true
        });
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateClientLinkAsync(
            OwnerId, graph.OrganizationId, Guid.NewGuid()));
    }

    [Fact]
    public async Task DisconnectClearsTelegramConnectionAndLink()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, clientLinkToken: "one-time-link");
        await using var context = fixture.CreateContext();
        var channelService = new Mock<IOrganizationChannelService>();
        channelService.Setup(service => service.RevokeClientAsync(
            graph.OrganizationId, graph.ClientId, "telegram", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => Assert.NotNull(context.Database.CurrentTransaction));
        var service = this.CreateService(
            context,
            new TestHttpMessageHandler(_ => this.Success("{}")),
            channelService: channelService);

        await service.DisconnectClientAsync(OwnerId, graph.OrganizationId, graph.ClientId);

        var client = await context.OrganizationClients.SingleAsync();
        Assert.Null(client.TelegramChatId);
        Assert.Null(client.TelegramConnectedAt);
        Assert.False(client.TelegramNotificationsEnabled);
        Assert.Null(client.TelegramLinkTokenHash);
        Assert.Null(client.TelegramLinkExpiresAt);
        channelService.Verify(service => service.RevokeClientAsync(
            graph.OrganizationId,
            graph.ClientId,
            "telegram",
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Once);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DisconnectClientAsync(
            OwnerId, graph.OrganizationId, Guid.NewGuid()));
    }

    [Fact]
    public async Task WebhookConnectsClientForMatchingSecretAndOneTimeLink()
    {
        var graph = await this.CreateOrganizationAsync(clientLinkToken: "one-time-link");
        var protector = new EphemeralDataProtectionProvider();
        var secret = "opencrm-webhook-secret";
        await using var context = fixture.CreateContext();
        context.OrganizationTelegramBots.Add(this.CreateBot(graph.OrganizationId, protector, secret));
        await context.SaveChangesAsync();
        var conversationService = new Mock<IOrganizationConversationService>();
        conversationService
            .Setup(service => service.BindTelegramClientAsync(
                It.IsAny<Guid>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = this.CreateService(
            context,
            new TestHttpMessageHandler(_ => this.Success("{}")),
            protector,
            conversationService: conversationService);
        using var update = JsonDocument.Parse("{\"message\":{\"date\":1792742400,\"chat\":{\"type\":\"private\",\"id\":987654},\"text\":\"/start one-time-link\"}}");

        Assert.False(await service.ProcessWebhookAsync(graph.OrganizationId, "wrong-secret", update.RootElement));
        Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, secret, update.RootElement));

        var client = await context.OrganizationClients.SingleAsync();
        Assert.Equal(987654, client.TelegramChatId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792742400).UtcDateTime, client.TelegramConnectedAt);
        Assert.True(client.TelegramNotificationsEnabled);
        Assert.Null(client.TelegramLinkTokenHash);
        Assert.Null(client.TelegramLinkExpiresAt);
        conversationService.Verify(service => service.BindTelegramClientAsync(
            graph.OrganizationId,
            123456,
            987654,
            graph.ClientId,
            DateTimeOffset.FromUnixTimeSeconds(1792742400).UtcDateTime,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WebhookRebindsChatAndDisconnectsItsPreviousClient()
    {
        var graph = await this.CreateOrganizationAsync();
        var protector = new EphemeralDataProtectionProvider();
        const string secret = "opencrm-webhook-secret";
        const string linkToken = "second-client-link";
        await using var context = fixture.CreateContext();
        var previousClient = await context.OrganizationClients.SingleAsync();
        var replacementClient = new OrganizationClient
        {
            OrganizationId = graph.OrganizationId,
            Name = "Клиент для перепривязки",
            TelegramLinkTokenHash = HashToken(linkToken),
            TelegramLinkExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        context.OrganizationClients.Add(replacementClient);
        context.OrganizationTelegramBots.Add(this.CreateBot(graph.OrganizationId, protector, secret));
        await context.SaveChangesAsync();
        var conversationService = new Mock<IOrganizationConversationService>();
        conversationService
            .Setup(service => service.BindTelegramClientAsync(
                It.IsAny<Guid>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = this.CreateService(
            context,
            new TestHttpMessageHandler(_ => this.Success("{}")),
            protector,
            conversationService: conversationService);
        using var update = JsonDocument.Parse($"{{\"message\":{{\"date\":1792742400,\"chat\":{{\"type\":\"private\",\"id\":987654}},\"text\":\"/start {linkToken}\"}}}}");

        Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, secret, update.RootElement));

        Assert.Null(previousClient.TelegramChatId);
        Assert.Null(previousClient.TelegramConnectedAt);
        Assert.False(previousClient.TelegramNotificationsEnabled);
        Assert.Equal(987654, replacementClient.TelegramChatId);
        Assert.True(replacementClient.TelegramNotificationsEnabled);
        Assert.Null(replacementClient.TelegramLinkTokenHash);
        Assert.Null(replacementClient.TelegramLinkExpiresAt);
    }

    [Fact]
    public async Task WebhookDoesNotRebindClientWhenConversationRejectsStaleStart()
    {
        var graph = await this.CreateOrganizationAsync();
        var protector = new EphemeralDataProtectionProvider();
        const string secret = "opencrm-webhook-secret";
        const string linkToken = "replacement-client-link";
        await using var context = fixture.CreateContext();
        var previousClient = await context.OrganizationClients.SingleAsync();
        var previousConnectedAt = previousClient.TelegramConnectedAt;
        var replacementClient = new OrganizationClient
        {
            OrganizationId = graph.OrganizationId,
            Name = "Клиент для перепривязки",
            TelegramLinkTokenHash = HashToken(linkToken),
            TelegramLinkExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        context.OrganizationClients.Add(replacementClient);
        context.OrganizationTelegramBots.Add(this.CreateBot(graph.OrganizationId, protector, secret));
        await context.SaveChangesAsync();
        var conversationService = new Mock<IOrganizationConversationService>();
        conversationService
            .Setup(service => service.BindTelegramClientAsync(
                It.IsAny<Guid>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = this.CreateService(
            context,
            new TestHttpMessageHandler(_ => this.Success("{}")),
            protector,
            conversationService: conversationService);
        using var update = JsonDocument.Parse(
            $"{{\"message\":{{\"date\":1792742400,\"chat\":{{\"type\":\"private\",\"id\":987654}},\"text\":\"/start {linkToken}\"}}}}");

        Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, secret, update.RootElement));

        Assert.Equal(987654, previousClient.TelegramChatId);
        Assert.Equal(previousConnectedAt, previousClient.TelegramConnectedAt);
        Assert.True(previousClient.TelegramNotificationsEnabled);
        Assert.Null(replacementClient.TelegramChatId);
        Assert.NotNull(replacementClient.TelegramLinkTokenHash);
        conversationService.Verify(service => service.BindTelegramClientAsync(
            graph.OrganizationId,
            123456,
            987654,
            replacementClient.Id,
            DateTimeOffset.FromUnixTimeSeconds(1792742400).UtcDateTime,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WebhookPersistsPrivateTextAndMediaMetadataThroughConversationService()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true);
        var protector = new EphemeralDataProtectionProvider();
        const string secret = "opencrm-webhook-secret";
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationTelegramBots.SingleAsync();
        bot.ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Protect(secret);
        await context.SaveChangesAsync();
        var conversationService = new Mock<IOrganizationConversationService>();
        conversationService
            .Setup(service => service.IngestTelegramMessageAsync(It.IsAny<IngestTelegramMessageDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = this.CreateService(
            context,
            new TestHttpMessageHandler(_ => this.Success("{}")),
            protector,
            conversationService: conversationService);
        using var update = JsonDocument.Parse("""
            {"update_id":71,"message":{"message_id":9,"date":1792742400,"chat":{"type":"private","id":987654},"from":{"id":987654,"first_name":"Иван","username":"ivan"},"caption":"Фото автомобиля","document":{"file_id":"file-id","file_unique_id":"unique-id","file_name":"photo.pdf","mime_type":"application/pdf","file_size":2345}}}
            """);

        Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, secret, update.RootElement));

        conversationService.Verify(service => service.IngestTelegramMessageAsync(
            It.Is<IngestTelegramMessageDto>(message => message.OrganizationId == graph.OrganizationId
                && message.BotId == 123456
                && message.UpdateId == 71
                && message.ProviderMessageId == 9
                && message.ChatId == 987654
                && message.SenderTelegramUserId == 987654
                && message.SenderUsername == "ivan"
                && message.SenderDisplayName == "Иван"
                && message.Text == "Фото автомобиля"
                && message.Attachments.Count == 1
                && message.Attachments[0].ProviderFileId == "file-id"
                && message.Attachments[0].ProviderFileUniqueId == "unique-id"
                && message.Attachments[0].SizeBytes == 2345), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("voice", "[Голосовое сообщение: просмотр этого формата пока недоступен]")]
    [InlineData("video", "[Видео: просмотр этого формата пока недоступен]")]
    [InlineData("video_note", "[Видеосообщение: просмотр этого формата пока недоступен]")]
    [InlineData("sticker", "[Стикер: просмотр этого формата пока недоступен]")]
    [InlineData("audio", "[Аудиофайл: просмотр этого формата пока недоступен]")]
    [InlineData("animation", "[GIF-анимация: просмотр этого формата пока недоступен]")]
    [InlineData("location", "[Геопозиция: просмотр этого формата пока недоступен]")]
    [InlineData("contact", "[Контакт: просмотр этого формата пока недоступен]")]
    [InlineData("venue", "[Место: просмотр этого формата пока недоступен]")]
    [InlineData("poll", "[Опрос: просмотр этого формата пока недоступен]")]
    [InlineData("unsupported", "[Сообщение Telegram: этот формат пока недоступен]")]
    public async Task WebhookPersistsTypedPlaceholderForUnsupportedPrivateMedia(string mediaType, string placeholder)
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true);
        var protector = new EphemeralDataProtectionProvider();
        const string secret = "opencrm-webhook-secret";
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationTelegramBots.SingleAsync();
        bot.ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Protect(secret);
        await context.SaveChangesAsync();
        var conversationService = new Mock<IOrganizationConversationService>();
        conversationService
            .Setup(service => service.IngestTelegramMessageAsync(It.IsAny<IngestTelegramMessageDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = this.CreateService(
            context,
            new TestHttpMessageHandler(_ => this.Success("{}")),
            protector,
            conversationService: conversationService);
        var messageContent = mediaType == "unsupported" ? "\"new_type\":{}" : $"\"{mediaType}\":{{}}";
        using var update = JsonDocument.Parse(
            $"{{\"update_id\":72,\"message\":{{\"message_id\":10,\"date\":1792742400,\"chat\":{{\"type\":\"private\",\"id\":987654}},\"from\":{{\"id\":987654}},{messageContent}}}}}");

        Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, secret, update.RootElement));

        conversationService.Verify(service => service.IngestTelegramMessageAsync(
            It.Is<IngestTelegramMessageDto>(message => message.Text == placeholder && message.Attachments.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WebhookIgnoresMalformedAndUnmatchedUpdatesAfterAuthenticatingTheRelay()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, clientLinkToken: "expired-link");
        var protector = new EphemeralDataProtectionProvider();
        const string secret = "opencrm-webhook-secret";
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationTelegramBots.SingleAsync();
        bot.ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Protect(secret);
        var client = await context.OrganizationClients.SingleAsync();
        client.TelegramLinkExpiresAt = DateTime.UtcNow.AddDays(-1);
        await context.SaveChangesAsync();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), protector);

        string[] updates =
        [
            "[]",
            "{}",
            "{\"message\":null}",
            "{\"message\":123}",
            "{\"message\":{}}",
            "{\"message\":{\"chat\":null}}",
            "{\"message\":{\"chat\":123}}",
            "{\"message\":{\"chat\":{}}}",
            "{\"message\":{\"chat\":{\"type\":123,\"id\":987654}}}",
            "{\"message\":{\"chat\":{\"type\":\"group\",\"id\":987654},\"text\":\"/start expired-link\"}}",
            "{\"message\":{\"chat\":{\"type\":\"private\"},\"text\":\"/start expired-link\"}}",
            "{\"message\":{\"chat\":{\"type\":\"private\",\"id\":-1},\"text\":\"/start expired-link\"}}",
            "{\"message\":{\"chat\":{\"type\":\"private\",\"id\":\"987654\"},\"text\":\"/start expired-link\"}}",
            "{\"message\":{\"chat\":{\"type\":\"private\",\"id\":9223372036854775808},\"text\":\"/start expired-link\"}}",
            "{\"message\":{\"chat\":{\"type\":\"private\",\"id\":987654},\"text\":123}}",
            "{\"message\":{\"chat\":{\"type\":\"private\",\"id\":987654},\"text\":\"hello\"}}",
            "{\"message\":{\"chat\":{\"type\":\"private\",\"id\":987654},\"text\":\"/start expired-link\"}}",
            "{\"message\":{\"chat\":{\"type\":\"private\",\"id\":987654},\"text\":\"/start   \"}}",
            "{\"my_chat_member\":null}",
            "{\"my_chat_member\":123}",
            "{\"my_chat_member\":{\"chat\":null}}",
            "{\"my_chat_member\":{\"chat\":{\"type\":\"group\",\"id\":987654},\"new_chat_member\":{\"status\":\"left\"}}}",
            "{\"my_chat_member\":{\"chat\":{\"type\":123,\"id\":987654},\"new_chat_member\":{\"status\":\"left\"}}}",
            "{\"my_chat_member\":{\"chat\":{\"type\":\"private\",\"id\":\"987654\"},\"new_chat_member\":{\"status\":\"left\"}}}",
            "{\"my_chat_member\":{\"chat\":{\"type\":\"private\",\"id\":9223372036854775808},\"new_chat_member\":{\"status\":\"left\"}}}",
            "{\"my_chat_member\":{\"chat\":{\"type\":\"private\",\"id\":987654},\"new_chat_member\":null}}",
            "{\"my_chat_member\":{\"chat\":{\"type\":\"private\",\"id\":987654},\"new_chat_member\":123}}",
            "{\"my_chat_member\":{\"chat\":{\"type\":\"private\",\"id\":987654},\"new_chat_member\":{\"status\":123}}}",
            "{\"my_chat_member\":{\"chat\":{\"type\":\"private\",\"id\":987654},\"new_chat_member\":{\"status\":\"member\"}}}",
            "{\"my_chat_member\":{\"chat\":{\"type\":\"private\",\"id\":987654},\"date\":-1,\"new_chat_member\":{\"status\":\"left\"}}}"
        ];

        foreach (var updateJson in updates)
        {
            using var update = JsonDocument.Parse(updateJson);
            Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, secret, update.RootElement));
        }

        using var emptyUpdate = JsonDocument.Parse("{}");
        Assert.False(await service.ProcessWebhookAsync(graph.OrganizationId, null, emptyUpdate.RootElement));
        Assert.False(await service.ProcessWebhookAsync(Guid.NewGuid(), secret, emptyUpdate.RootElement));
        Assert.False((await context.OrganizationClients.SingleAsync()).TelegramNotificationsEnabled);
    }

    [Fact]
    public async Task DisabledBotAcknowledgesWebhooksWithoutProcessingEvents()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true);
        var protector = new EphemeralDataProtectionProvider();
        const string secret = "opencrm-webhook-secret";
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationTelegramBots.SingleAsync();
        bot.IsEnabled = false;
        bot.ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Protect(secret);
        await context.SaveChangesAsync();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), protector);
        using var update = JsonDocument.Parse("{\"message\":{\"chat\":{\"type\":\"private\",\"id\":987654},\"text\":\"/start expired-link\"}}");

        Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, secret, update.RootElement));
        Assert.True((await context.OrganizationClients.SingleAsync()).TelegramNotificationsEnabled);
    }

    [Theory]
    [InlineData("kicked", 4102444800L, false)]
    [InlineData("left", 4102444800L, false)]
    [InlineData("left", -1L, false)]
    [InlineData("left", 1L, true)]
    public async Task BlockedBotDisablesOnlyChatsConnectedBeforeTheEvent(string status, long timestamp, bool shouldRemainEnabled)
    {
        var graph = await this.CreateOrganizationAsync();
        var protector = new EphemeralDataProtectionProvider();
        var secret = "opencrm-webhook-secret";
        await using var context = fixture.CreateContext();
        context.OrganizationTelegramBots.Add(this.CreateBot(graph.OrganizationId, protector, secret));
        await context.SaveChangesAsync();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), protector);
        using var update = JsonDocument.Parse($"{{\"my_chat_member\":{{\"chat\":{{\"type\":\"private\",\"id\":987654}},\"date\":{timestamp},\"new_chat_member\":{{\"status\":\"{status}\"}}}}}}");

        Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, secret, update.RootElement));

        Assert.Equal(shouldRemainEnabled,
            (await context.OrganizationClients.SingleAsync(client => client.TelegramChatId == 987654)).TelegramNotificationsEnabled);
    }

    [Fact]
    public async Task CredentialRequiresEnabledFeatureMatchingChatAndCurrentNotification()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, botEnabled: true, notification: true);
        var protector = new EphemeralDataProtectionProvider();
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationTelegramBots.SingleAsync();
        bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Protect("bot-token");
        await context.SaveChangesAsync();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), protector);

        Assert.Equal("bot-token", await service.GetBotCredentialAsync(
            graph.OrganizationId, graph.NotificationKey!, "987654", CancellationToken.None));
        Assert.Null(await service.GetBotCredentialAsync(graph.OrganizationId, graph.NotificationKey!, "not-a-number", CancellationToken.None));
        Assert.Null(await service.GetBotCredentialAsync(graph.OrganizationId, graph.NotificationKey!, "987655", CancellationToken.None));
        Assert.Null(await service.GetBotCredentialAsync(graph.OrganizationId, "telegram:unknown", "987654", CancellationToken.None));

        var disabled = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), protector,
            new Dictionary<string, string?> { ["TelegramOrganizationNotifications:Enabled"] = "false" });
        Assert.Null(await disabled.GetBotCredentialAsync(graph.OrganizationId, graph.NotificationKey!, "987654", CancellationToken.None));
    }

    [Fact]
    public async Task CredentialIsNotReturnedWhenVehicleIsDeletedDuringLookup()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, botEnabled: true, notification: true);
        var protector = new EphemeralDataProtectionProvider();
        await using (var setup = fixture.CreateContext())
        {
            var bot = await setup.OrganizationTelegramBots.SingleAsync();
            bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Protect("bot-token");
            await setup.SaveChangesAsync();
        }

        var deletion = new ConcurrentDeleteInterceptor("Vehicles", async () =>
        {
            await using var deleteContext = fixture.CreateContext();
            await deleteContext.Vehicles.ExecuteDeleteAsync();
        });
        await using var context = fixture.CreateContext(deletion);
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), protector);

        var credential = await service.GetBotCredentialAsync(
            graph.OrganizationId, graph.NotificationKey!, "987654", CancellationToken.None);

        Assert.True(deletion.HasDeleted);
        Assert.Null(credential);
    }

    [Theory]
    [InlineData("client-deleted")]
    [InlineData("client-missing")]
    [InlineData("notifications-disabled")]
    [InlineData("chat-changed")]
    [InlineData("not-connected")]
    [InlineData("connected-after-notification")]
    [InlineData("vehicle-without-organization")]
    [InlineData("vehicle-client-mismatch")]
    [InlineData("maintenance-cancelled")]
    [InlineData("booking-changed-cancelled")]
    [InlineData("cancellation-not-cancelled")]
    [InlineData("maintenance-missing")]
    [InlineData("ready-not-ready")]
    [InlineData("ready-handed-over")]
    [InlineData("ready-disabled")]
    [InlineData("booking-created-completed")]
    [InlineData("later-notification")]
    [InlineData("bot-disabled")]
    [InlineData("bot-missing")]
    [InlineData("booking-disabled")]
    [InlineData("organization-inactive")]
    [InlineData("organization-deleted")]
    [InlineData("organization-archived")]
    public async Task CredentialIsNotReturnedWhenItsNotificationIsNoLongerEligible(string scenario)
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, botEnabled: true, notification: true);
        var protector = new EphemeralDataProtectionProvider();
        await using var context = fixture.CreateContext();
        var client = await context.OrganizationClients.SingleAsync();
        var maintenance = await context.Maintenance.SingleAsync();
        var vehicle = await context.Vehicles.SingleAsync();
        var bot = await context.OrganizationTelegramBots.SingleAsync();
        var organization = await context.Organizations.SingleAsync();
        var notification = await context.TelegramClientNotifications.SingleAsync();
        bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Protect("bot-token");

        switch (scenario)
        {
            case "client-deleted":
                client.IsDeleted = true;
                break;
            case "client-missing":
                notification.ClientId = Guid.NewGuid();
                break;
            case "notifications-disabled":
                client.TelegramNotificationsEnabled = false;
                break;
            case "chat-changed":
                client.TelegramChatId = 987655;
                break;
            case "not-connected":
                client.TelegramConnectedAt = null;
                break;
            case "connected-after-notification":
                client.TelegramConnectedAt = DateTime.UtcNow.AddDays(1);
                break;
            case "vehicle-without-organization":
                vehicle.OrganizationId = null;
                break;
            case "vehicle-client-mismatch":
                vehicle.OrganizationClientId = null;
                break;
            case "maintenance-cancelled":
                maintenance.Status = MaintenanceStatus.Cancelled;
                break;
            case "booking-changed-cancelled":
                notification.EventCode = "booking_changed";
                maintenance.Status = MaintenanceStatus.Cancelled;
                break;
            case "cancellation-not-cancelled":
                notification.EventCode = "booking_cancelled";
                break;
            case "maintenance-missing":
                notification.MaintenanceId = Guid.NewGuid();
                break;
            case "ready-not-ready":
                notification.EventCode = "ready";
                break;
            case "ready-handed-over":
                notification.EventCode = "ready";
                maintenance.Status = MaintenanceStatus.Completed;
                maintenance.ReadyForHandoverAt = DateTime.UtcNow.AddMinutes(-1);
                maintenance.HandedOverAt = DateTime.UtcNow;
                break;
            case "ready-disabled":
                notification.EventCode = "ready";
                maintenance.Status = MaintenanceStatus.Completed;
                maintenance.ReadyForHandoverAt = DateTime.UtcNow.AddMinutes(-1);
                bot.NotifyReady = false;
                break;
            case "booking-created-completed":
                notification.EventCode = "booking_created";
                maintenance.Status = MaintenanceStatus.Completed;
                break;
            case "later-notification":
                context.TelegramClientNotifications.Add(new TelegramClientNotification
                {
                    OrganizationId = graph.OrganizationId,
                    ClientId = graph.ClientId,
                    ChatId = 987654,
                    MaintenanceId = maintenance.Id,
                    EventCode = "booking_changed",
                    NotificationKey = $"telegram:{Guid.NewGuid():N}",
                    Body = "Новое уведомление",
                    Status = "queued",
                    NextAttemptAt = DateTime.UtcNow
                });
                break;
            case "bot-disabled":
                bot.IsEnabled = false;
                break;
            case "bot-missing":
                context.OrganizationTelegramBots.Remove(bot);
                break;
            case "booking-disabled":
                bot.NotifyBooking = false;
                break;
            case "organization-inactive":
                organization.IsActive = false;
                break;
            case "organization-deleted":
                organization.IsDeleted = true;
                break;
            case "organization-archived":
                organization.IsArchived = true;
                break;
        }

        await context.SaveChangesAsync();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), protector);

        Assert.Null(await service.GetBotCredentialAsync(
            graph.OrganizationId, graph.NotificationKey!, "987654", CancellationToken.None));
    }

    [Fact]
    public async Task ReadyAndCancellationNotificationsRequireTheirMatchingMaintenanceState()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, botEnabled: true, notification: true);
        var protector = new EphemeralDataProtectionProvider();
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationTelegramBots.SingleAsync();
        bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Protect("bot-token");
        var maintenance = await context.Maintenance.SingleAsync();
        var notification = await context.TelegramClientNotifications.SingleAsync();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), protector);

        notification.EventCode = "ready";
        maintenance.Status = MaintenanceStatus.Completed;
        maintenance.ReadyForHandoverAt = DateTime.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();
        Assert.Equal("bot-token", await service.GetBotCredentialAsync(
            graph.OrganizationId, graph.NotificationKey!, "987654", CancellationToken.None));

        maintenance.HandedOverAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        Assert.Null(await service.GetBotCredentialAsync(
            graph.OrganizationId, graph.NotificationKey!, "987654", CancellationToken.None));

        notification.EventCode = "booking_cancelled";
        maintenance.Status = MaintenanceStatus.Cancelled;
        maintenance.ReadyForHandoverAt = null;
        maintenance.HandedOverAt = null;
        await context.SaveChangesAsync();
        Assert.Equal("bot-token", await service.GetBotCredentialAsync(
            graph.OrganizationId, graph.NotificationKey!, "987654", CancellationToken.None));
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("skipped")]
    [InlineData("sent")]
    [InlineData("simulated")]
    public async Task TerminalNotificationDoesNotExposeBotCredential(string status)
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, botEnabled: true, notification: true, notificationStatus: status);
        var protector = new EphemeralDataProtectionProvider();
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationTelegramBots.SingleAsync();
        bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Protect("bot-token");
        await context.SaveChangesAsync();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), protector);

        Assert.Null(await service.GetBotCredentialAsync(graph.OrganizationId, graph.NotificationKey!, "987654", CancellationToken.None));
    }

    private async Task<OrganizationGraph> CreateOrganizationAsync(
        string ownerId = OwnerId,
        bool includeBot = false,
        string username ="test_bot",
        bool botEnabled = true,
        bool notification = false,
        string notificationStatus = "queued",
        string? clientLinkToken = null)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Id = ownerId, UserName = $"{ownerId}@example.test" };
        var organization = new Organization { Name = "Тестовая организация", OwnerUser = user };
        var client = new OrganizationClient
        {
            Organization = organization,
            Name = "Тестовый клиент",
            TelegramChatId = 987654,
            TelegramConnectedAt = DateTime.UtcNow.AddMinutes(-1),
            TelegramNotificationsEnabled = true,
            TelegramLinkTokenHash = clientLinkToken is null ? null : HashToken(clientLinkToken),
            TelegramLinkExpiresAt = clientLinkToken is null ? null : DateTime.UtcNow.AddDays(1)
        };
        var vehicle = new Vehicle
        {
            User = user,
            Organization = organization,
            OrganizationClient = client,
            RegistrationPlate = "А123АА"
        };
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Planned,
            Date = DateTime.UtcNow.Date,
            PlannedDate = DateTime.UtcNow.Date
        };
        context.Maintenance.Add(maintenance);
        if (includeBot)
        {
            context.OrganizationTelegramBots.Add(new OrganizationTelegramBot
            {
                Organization = organization,
                Username = username,
                TelegramBotId = 123456,
                ProtectedToken = "protected-token",
                ProtectedWebhookSecret = "protected-secret",
                IsEnabled = botEnabled
            });
        }

        string? notificationKey = null;
        if (notification)
        {
            notificationKey = $"telegram:{Guid.NewGuid():N}";
            context.TelegramClientNotifications.Add(new TelegramClientNotification
            {
                OrganizationId = organization.Id,
                ClientId = client.Id,
                ChatId = client.TelegramChatId!.Value,
                MaintenanceId = maintenance.Id,
                EventCode = "booking_created",
                NotificationKey = notificationKey,
                Body = "Тестовое уведомление",
                Status = notificationStatus,
                NextAttemptAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();

        return new OrganizationGraph(organization.Id, client.Id, notificationKey);
    }

    private OrganizationTelegramBot CreateBot(Guid organizationId, IDataProtectionProvider protector, string secret) => new()
    {
        OrganizationId = organizationId,
        Username ="test_bot",
        TelegramBotId = 123456,
        ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Protect("bot-token"),
        ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationTelegramBot.v1").Protect(secret),
        IsEnabled = true
    };

    private OrganizationTelegramService CreateService(
        OpenCRMContext context,
        HttpMessageHandler handler,
        IDataProtectionProvider? dataProtectionProvider = null,
        Dictionary<string, string?>? configuration = null,
        Mock<IOrganizationConversationService>? conversationService = null,
        Mock<IOrganizationChannelService>? channelService = null,
        bool hasClientMessagingFeature = true)
    {
        var subscriptionService = new Mock<IOrganizationSubscriptionService>();
        subscriptionService
            .Setup(service => service.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscriptionService
            .Setup(service => service.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscriptionService
            .Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), Helpers.FeatureCodes.ClientMessaging))
            .ReturnsAsync(hasClientMessagingFeature);
        var values = new Dictionary<string, string?>
        {
            ["TelegramOrganizationNotifications:Enabled"] = "true",
            ["TelegramOrganizationNotifications:RelayKey"] = RelayKey,
            ["TelegramOrganizationNotifications:RelayBaseUrl"] = "https://relay.example/api/"
        };
        if (configuration is not null)
        {
            foreach (var (key, value) in configuration)
            {
                values[key] = value;
            }
        }

        var actualConversationService = conversationService ?? new Mock<IOrganizationConversationService>();
        actualConversationService
            .Setup(service => service.SetTelegramChatActiveAsync(
                It.IsAny<Guid>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<bool>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return new OrganizationTelegramService(
            context,
            dataProtectionProvider ?? new EphemeralDataProtectionProvider(),
            new TestHttpClientFactory(handler),
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            subscriptionService.Object,
            actualConversationService.Object,
            channelService?.Object ?? new Mock<IOrganizationChannelService>().Object);
    }

    private HttpResponseMessage Success(string json) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Content = request.Content is null ? null : new StringContent(request.Content.ReadAsStringAsync().GetAwaiter().GetResult())
        };
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed record OrganizationGraph(Guid OrganizationId, Guid ClientId, string? NotificationKey);

    private sealed class TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.RequestCount++;

            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://relay.example/api/")
        };
    }
}
