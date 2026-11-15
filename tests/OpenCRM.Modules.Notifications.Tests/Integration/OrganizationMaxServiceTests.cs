using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;
using OpenCRM.Modules.Notifications.Max;
using OpenCRM.Modules.Notifications;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

public sealed class OrganizationMaxServiceTests(ClientNotificationDatabaseFixture fixture)
    : IClassFixture<ClientNotificationDatabaseFixture>, IAsyncLifetime
{
    private const string OwnerId = "max-owner";

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task OwnerCanReadSettingsAndNonOwnerCannot()
    {
        var graph = await this.CreateOrganizationAsync();
        var protector = new EphemeralDataProtectionProvider();
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), protector);

        var settings = JsonSerializer.SerializeToElement(await service.GetSettingsAsync(OwnerId, graph.OrganizationId));

        Assert.False(settings.GetProperty("isConfigured").GetBoolean());
        Assert.False(settings.GetProperty("isEnabled").GetBoolean());
        Assert.True(settings.GetProperty("notifyBooking").GetBoolean());
        Assert.True(settings.GetProperty("notifyReady").GetBoolean());
        Assert.Equal($"https://opencrm.example/api/webhooks/max/{graph.OrganizationId}", settings.GetProperty("webhookUrl").GetString());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetSettingsAsync("another-user", graph.OrganizationId));
    }

    [Fact]
    public async Task FreePlanCannotConnectMaxBotButCanKeepReadingSettings()
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), hasClientMessagingFeature: false);

        var settings = JsonSerializer.SerializeToElement(await service.GetSettingsAsync(OwnerId, graph.OrganizationId));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
            OwnerId, graph.OrganizationId, "bot-token", true, true, true, CancellationToken.None));

        Assert.False(settings.GetProperty("isConfigured").GetBoolean());
        Assert.Contains("PRO", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await context.OrganizationMaxBots.ToListAsync());
    }

    [Fact]
    public async Task SettingsRejectMissingBaseUrl()
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = this.CreateService(
            context,
            new TestHttpMessageHandler(_ => this.Success("{}")),
            configuration: new Dictionary<string, string?> { ["AppSettings:BaseUrl"] = null });

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetSettingsAsync(OwnerId, graph.OrganizationId));
    }

    [Fact]
    public async Task OwnerCanSaveBotSettingsThroughTheMaxApiAndProtectTheStoredSecrets()
    {
        var graph = await this.CreateOrganizationAsync();
        var protector = new EphemeralDataProtectionProvider();
        var requests = new List<HttpRequestMessage>();
        using var handler = new TestHttpMessageHandler(request =>
        {
            requests.Add(CloneRequest(request));
            return request.RequestUri!.AbsolutePath.EndsWith("/me", StringComparison.Ordinal)
                ? this.Success("{\"is_bot\":true,\"user_id\":101,\"username\":\"@Test_Bot\"}")
                : this.Success("{\"success\":true}");
        });
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, handler, protector);

        await service.SaveSettingsAsync(OwnerId, graph.OrganizationId, "  secret-token  ", true, false, true, CancellationToken.None);

        var bot = await context.OrganizationMaxBots.SingleAsync();
        Assert.Equal("test_bot", bot.Username);
        Assert.True(bot.IsEnabled);
        Assert.False(bot.NotifyBooking);
        Assert.True(bot.NotifyReady);
        Assert.NotEqual("secret-token", bot.ProtectedToken);
        Assert.Equal("secret-token", protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Unprotect(bot.ProtectedToken));
        Assert.NotEqual("secret-token", bot.ProtectedWebhookSecret);
        var settings = JsonSerializer.SerializeToElement(await service.GetSettingsAsync(OwnerId, graph.OrganizationId));
        Assert.True(settings.GetProperty("isConfigured").GetBoolean());
        Assert.True(settings.GetProperty("isEnabled").GetBoolean());
        Assert.False(settings.GetProperty("notifyBooking").GetBoolean());
        Assert.True(settings.GetProperty("notifyReady").GetBoolean());
        Assert.Equal(2, requests.Count);
        Assert.Equal("secret-token", requests[0].Headers.Authorization?.Parameter ?? requests[0].Headers.GetValues("Authorization").Single());
        Assert.Equal("/subscriptions", requests[1].RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(await requests[1].Content!.ReadAsStringAsync());
        Assert.Equal($"https://opencrm.example/api/webhooks/max/{graph.OrganizationId}", body.RootElement.GetProperty("url").GetString());
        foreach (var request in requests)
        {
            request.Dispose();
        }
    }

    [Theory]
    [InlineData("http://opencrm.example")]
    [InlineData("not-a-url")]
    public async Task MissingTokenCannotCreateBotAndInvalidWebhookBaseUrlIsRejected(string baseUrl)
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")));

        var missingToken = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveSettingsAsync(OwnerId, graph.OrganizationId, " ", true, true, true, CancellationToken.None));
        Assert.Equal("Укажите токен бота MAX.", missingToken.Message);

        var invalidConfiguration = new Dictionary<string, string?> { ["AppSettings:BaseUrl"] = baseUrl };
        var invalidService = this.CreateService(
            context,
            new TestHttpMessageHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/me", StringComparison.Ordinal)
                ? this.Success("{\"is_bot\":true,\"username\":\"test_bot\"}")
                : this.Success("{\"success\":true}")),
            configuration: invalidConfiguration);
        var invalidUrl = await Assert.ThrowsAsync<InvalidOperationException>(() => invalidService.SaveSettingsAsync(
            OwnerId, graph.OrganizationId, "token", true, true, true, CancellationToken.None));

        Assert.Equal("Для MAX нужен корректный HTTPS AppSettings:BaseUrl.", invalidUrl.Message);
    }

    [Fact]
    public async Task ExistingBotCanBeDisabledWithoutMakingAnExternalRequest()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true);
        var handler = new TestHttpMessageHandler(_ => throw new InvalidOperationException("Внешний запрос не ожидался."));
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, handler);

        await service.SaveSettingsAsync(OwnerId, graph.OrganizationId, null, false, false, true, CancellationToken.None);

        var bot = await context.OrganizationMaxBots.SingleAsync();
        Assert.False(bot.IsEnabled);
        Assert.False(bot.NotifyBooking);
        Assert.True(bot.NotifyReady);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ExistingBotCannotBeDuplicatedAcrossOrganizations()
    {
        await this.CreateOrganizationAsync(includeBot: true, username: "same_bot");
        var secondGraph = await this.CreateOrganizationAsync("max-owner-2");
        using var handler = new TestHttpMessageHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/me", StringComparison.Ordinal)
            ? this.Success("{\"is_bot\":true,\"user_id\":101,\"username\":\"same_bot\"}")
            : this.Success("{\"success\":true}"));
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
            "max-owner-2", secondGraph.OrganizationId, "another-token", true, true, true, CancellationToken.None));

        Assert.Equal("Этот бот MAX уже подключён к другой организации.", exception.Message);
        Assert.Equal(1, await context.OrganizationMaxBots.CountAsync());
    }

    [Theory]
    [InlineData("{\"is_bot\":false,\"username\":\"bad\"}", "MAX не подтвердил подписку на события бота.")]
    [InlineData("{\"success\":false}", "MAX не подтвердил подписку на события бота.")]
    public async Task MaxApiResponsesMustConfirmTheBotAndWebhook(string response, string error)
    {
        var graph = await this.CreateOrganizationAsync();
        using var handler = new TestHttpMessageHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/me", StringComparison.Ordinal)
            ? this.Success("{\"is_bot\":true,\"user_id\":102,\"username\":\"test_bot\"}")
            : this.Success(response));
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
            OwnerId, graph.OrganizationId, "token", true, true, true, CancellationToken.None));

        Assert.Equal(error, exception.Message);
        Assert.False(await context.OrganizationMaxBots.AnyAsync());
    }

    [Fact]
    public async Task HttpRequestFailureDuringMaxSetupIsReportedAndDoesNotPersistSettings()
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        using var handler = new TestHttpMessageHandler(_ => throw new HttpRequestException());
        var service = this.CreateService(context, handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
            OwnerId, graph.OrganizationId, "token", true, true, true, CancellationToken.None));

        Assert.Equal("MAX не подтвердил подключение бота. Проверьте токен и повторите попытку.", exception.Message);
        Assert.False(await context.OrganizationMaxBots.AnyAsync());
    }

    [Theory]
    [InlineData("status", "MAX не принял токен бота.")]
    [InlineData("body", "MAX не вернул имя бота.")]
    public async Task MaxMustReturnAnAcceptedBotResponse(string responseType, string expectedMessage)
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        using var handler = new TestHttpMessageHandler(_ => responseType == "status"
            ? new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
            : this.Success("{\"is_bot\":false,\"username\":\"test_bot\"}"));
        var service = this.CreateService(context, handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
            OwnerId, graph.OrganizationId, "token", true, true, true, CancellationToken.None));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.False(await context.OrganizationMaxBots.AnyAsync());
    }

    [Theory]
    [InlineData("status", "MAX не принял адрес вебхука. Проверьте доступность HTTPS-сайта.")]
    [InlineData("body", "MAX не подтвердил подписку на события бота.")]
    public async Task MaxMustAcceptWebhookRegistration(string failure, string expectedMessage)
    {
        var graph = await this.CreateOrganizationAsync();
        await using var context = fixture.CreateContext();
        using var handler = new TestHttpMessageHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/me", StringComparison.Ordinal)
            ? this.Success("{\"is_bot\":true,\"user_id\":103,\"username\":\"test_bot\"}")
            : failure == "status"
                ? new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
                : this.Success("{\"success\":false}"));
        var service = this.CreateService(context, handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveSettingsAsync(
            OwnerId, graph.OrganizationId, "token", true, true, true, CancellationToken.None));

        Assert.Equal(expectedMessage, exception.Message);
        Assert.False(await context.OrganizationMaxBots.AnyAsync());
    }

    [Fact]
    public async Task ChangingBotUsernameDisconnectsPreviouslyLinkedClients()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, username: "old_bot");
        var protector = new EphemeralDataProtectionProvider();
        using var handler = new TestHttpMessageHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/me", StringComparison.Ordinal)
            ? this.Success("{\"is_bot\":true,\"user_id\":104,\"username\":\"new_bot\"}")
            : this.Success("{\"success\":true}"));
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationMaxBots.SingleAsync();
        bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect("old-token");
        bot.ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect("old-secret");
        await context.SaveChangesAsync();
        var service = this.CreateService(context, handler, protector);

        await service.SaveSettingsAsync(OwnerId, graph.OrganizationId, "new-token", true, true, true, CancellationToken.None);

        var client = await context.OrganizationClients.SingleAsync();
        Assert.Null(client.MaxUserId);
        Assert.Null(client.MaxConnectedAt);
        Assert.False(client.MaxNotificationsEnabled);
        Assert.Equal("new_bot", bot.Username);
    }

    [Fact]
    public async Task ExistingMaxBotCanBeReenabledWithItsProtectedCredentials()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, username: "test_bot", botEnabled: false);
        var protector = new EphemeralDataProtectionProvider();
        var requests = new List<HttpRequestMessage>();
        using var handler = new TestHttpMessageHandler(request =>
        {
            requests.Add(CloneRequest(request));
            return request.RequestUri!.AbsolutePath.EndsWith("/me", StringComparison.Ordinal)
                ? this.Success("{\"is_bot\":true,\"user_id\":105,\"username\":\"test_bot\"}")
                : this.Success("{\"success\":true}");
        });
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationMaxBots.SingleAsync();
        var secret = "opencrm-webhook-secret";
        bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect("saved-token");
        bot.ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect(secret);
        await context.SaveChangesAsync();
        var service = this.CreateService(context, handler, protector);

        await service.SaveSettingsAsync(OwnerId, graph.OrganizationId, null, true, true, true, CancellationToken.None);

        Assert.True(bot.IsEnabled);
        Assert.Equal("saved-token", protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Unprotect(bot.ProtectedToken));
        using var webhook = JsonDocument.Parse(await requests[1].Content!.ReadAsStringAsync());
        Assert.Equal(secret, webhook.RootElement.GetProperty("secret").GetString());
        foreach (var request in requests)
        {
            request.Dispose();
        }
    }

    [Fact]
    public async Task LinkCreationReturnsOneTimeTokenAndStoresOnlyItsHash()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, username: "test_bot");
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")));

        var link = await service.CreateClientLinkAsync(OwnerId, graph.OrganizationId, graph.ClientId);

        Assert.StartsWith("https://max.ru/test_bot?start=", link, StringComparison.Ordinal);
        var token = link[(link.IndexOf("?start=", StringComparison.Ordinal) + 7)..];
        var client = await context.OrganizationClients.SingleAsync();
        Assert.NotEqual(token, client.MaxLinkTokenHash);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), client.MaxLinkTokenHash);
        Assert.InRange(client.MaxLinkExpiresAt!.Value, DateTime.UtcNow.AddDays(6), DateTime.UtcNow.AddDays(8));
    }

    [Fact]
    public async Task ClientLinkRequiresEnabledBotAndExistingClient()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, botEnabled: false);
        await using var context = fixture.CreateContext();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")));

        var disabledBot = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateClientLinkAsync(OwnerId, graph.OrganizationId, graph.ClientId));
        Assert.Equal("Сначала подключите бота MAX в настройках организации.", disabledBot.Message);

        var enabledBot = await context.OrganizationMaxBots.SingleAsync();
        enabledBot.IsEnabled = true;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.CreateClientLinkAsync(OwnerId, graph.OrganizationId, Guid.NewGuid()));
    }

    [Fact]
    public async Task DisconnectClearsLinkAndNotificationCredentials()
    {
        var graph = await this.CreateOrganizationAsync(clientLinkToken: "hashed-link");
        await using var context = fixture.CreateContext();
        var channels = new Mock<IOrganizationChannelService>();
        channels.Setup(channel => channel.RevokeClientAsync(
                graph.OrganizationId, graph.ClientId, "max", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => Assert.NotNull(context.Database.CurrentTransaction));
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), channelServiceMock: channels);

        await service.DisconnectClientAsync(OwnerId, graph.OrganizationId, graph.ClientId);

        var client = await context.OrganizationClients.SingleAsync();
        Assert.Null(client.MaxUserId);
        Assert.Null(client.MaxConnectedAt);
        Assert.False(client.MaxNotificationsEnabled);
        Assert.Null(client.MaxLinkTokenHash);
        Assert.Null(client.MaxLinkExpiresAt);
        channels.Verify(channel => channel.RevokeClientAsync(
            graph.OrganizationId, graph.ClientId, "max", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DisconnectClientAsync(
            OwnerId, graph.OrganizationId, Guid.NewGuid()));
    }

    [Fact]
    public async Task ClientAndMaintenanceStatusIncludeTheirCurrentNotifications()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, notification: true);
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationMaxBots.SingleAsync();
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
        bot.IsEnabled = false;
        await context.SaveChangesAsync();
        var disconnectedBotStatus = JsonSerializer.SerializeToElement(await service.GetClientStatusAsync(
            OwnerId, graph.OrganizationId, graph.ClientId));
        Assert.False(disconnectedBotStatus.GetProperty("isBotConnected").GetBoolean());
        client.MaxNotificationsEnabled = false;
        await context.SaveChangesAsync();
        var notificationsDisabledStatus = JsonSerializer.SerializeToElement(await service.GetClientStatusAsync(
            OwnerId, graph.OrganizationId, graph.ClientId));
        Assert.False(notificationsDisabledStatus.GetProperty("isClientConnected").GetBoolean());
        client.MaxNotificationsEnabled = true;
        client.MaxUserId = null;
        await context.SaveChangesAsync();
        var disconnectedClientStatus = JsonSerializer.SerializeToElement(await service.GetClientStatusAsync(
            OwnerId, graph.OrganizationId, graph.ClientId));
        Assert.False(disconnectedClientStatus.GetProperty("isClientConnected").GetBoolean());
        context.OrganizationMaxBots.Remove(bot);
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
    public async Task WebhookConnectsClientOnlyWithMatchingSecretAndUnexpiredLink()
    {
        var graph = await this.CreateOrganizationAsync(clientLinkToken: "one-time-link");
        var dataProtection = new EphemeralDataProtectionProvider();
        var secret = "opencrm-webhook-secret";
        await using var context = fixture.CreateContext();
        var previousClient = new OrganizationClient
        {
            OrganizationId = graph.OrganizationId,
            Name = "Прежний клиент",
            MaxUserId = 123456,
            MaxConnectedAt = DateTime.UtcNow.AddMinutes(-5),
            MaxNotificationsEnabled = true
        };
        context.OrganizationClients.Add(previousClient);
        context.OrganizationMaxBots.Add(new OrganizationMaxBot
        {
            OrganizationId = graph.OrganizationId,
            Username ="test_bot",
            ProtectedToken = dataProtection.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect("bot-token"),
            ProtectedWebhookSecret = dataProtection.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect(secret),
            IsEnabled = true
        });
        await context.SaveChangesAsync();
        var channels = new Mock<IOrganizationChannelService>();
        var connectionId = Guid.NewGuid();
        channels.Setup(channel => channel.EnsureConnectionAsync(
                It.IsAny<EnsureOrganizationChannelConnectionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectionId);
        channels.Setup(channel => channel.BindClientAsync(
                graph.OrganizationId,
                connectionId,
                "123456",
                "123456",
                graph.ClientId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Callback(() => Assert.NotNull(context.Database.CurrentTransaction));
        var service = this.CreateService(context, new TestHttpMessageHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/me", StringComparison.Ordinal)
            ? this.Success("{\"is_bot\":true,\"user_id\":101,\"username\":\"test_bot\"}")
            : this.Success("{\"success\":true}")), dataProtection, channelServiceMock: channels);
        using var update = JsonDocument.Parse("{\"update_type\":\"bot_started\",\"payload\":\"one-time-link\",\"user\":{\"user_id\":123456}} ");

        Assert.False(await service.ProcessWebhookAsync(graph.OrganizationId, "wrong-secret", update.RootElement));
        Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, secret, update.RootElement));

        var client = await context.OrganizationClients.SingleAsync(item => item.Id == graph.ClientId);
        Assert.Equal(123456, client.MaxUserId);
        Assert.True(client.MaxNotificationsEnabled);
        Assert.Null(client.MaxLinkTokenHash);
        Assert.Null(client.MaxLinkExpiresAt);
        Assert.Null(previousClient.MaxUserId);
        Assert.Null(previousClient.MaxConnectedAt);
        Assert.False(previousClient.MaxNotificationsEnabled);
    }

    [Theory]
    [InlineData(4102444800000L, false)]
    [InlineData(1L, true)]
    public async Task WebhookStopDisablesOnlyClientsConnectedBeforeTheEventAndMissingBotReturnsFalse(
        long timestamp,
        bool shouldRemainEnabled)
    {
        var graph = await this.CreateOrganizationAsync();
        var protector = new EphemeralDataProtectionProvider();
        var secret = "opencrm-webhook-secret";
        await using var context = fixture.CreateContext();
        context.OrganizationClients.Add(new OrganizationClient
        {
            OrganizationId = graph.OrganizationId,
            Name = "Другой клиент",
            MaxUserId = 123457,
            MaxConnectedAt = DateTime.UtcNow.AddMinutes(-1),
            MaxNotificationsEnabled = true
        });
        context.OrganizationMaxBots.Add(new OrganizationMaxBot
        {
            OrganizationId = graph.OrganizationId,
            Username ="test_bot",
            ProtectedToken = "unused",
            ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect(secret),
            IsEnabled = true
        });
        await context.SaveChangesAsync();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), protector);
        using var update = JsonDocument.Parse($"{{\"update_type\":\"bot_stopped\",\"timestamp\":{timestamp},\"user\":{{\"user_id\":123456}}}} ");

        Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, secret, update.RootElement));

        var matchingClient = await context.OrganizationClients.SingleAsync(client => client.Id == graph.ClientId);
        var otherClient = await context.OrganizationClients.SingleAsync(client => client.MaxUserId == 123457);
        Assert.Equal(shouldRemainEnabled, matchingClient.MaxNotificationsEnabled);
        Assert.True(otherClient.MaxNotificationsEnabled);
        Assert.False(await service.ProcessWebhookAsync(Guid.NewGuid(), secret, update.RootElement));
    }

    [Fact]
    public async Task PrivateMessageIsIngestedThroughTheVerifiedMaxConnection()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, username: "test_bot");
        var protector = new EphemeralDataProtectionProvider();
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationMaxBots.SingleAsync();
        bot.ImmutableProviderBotId = "201";
        bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect("max-token");
        bot.ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect("max-secret");
        await context.SaveChangesAsync();

        var maxApiRequestCount = 0;
        var channels = new Mock<IOrganizationChannelService>();
        var ingestedMessages = new List<ChannelMessageReceivedDto>();
        var connectionId = Guid.NewGuid();
        channels.Setup(service => service.EnsureConnectionAsync(
                It.Is<EnsureOrganizationChannelConnectionDto>(item => item.ProviderCode == "max"
                    && item.ImmutableProviderIdentity == "201" && item.IsVerified),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectionId);
        channels.Setup(service => service.IngestMessageAsync(It.IsAny<ChannelMessageReceivedDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Callback<ChannelMessageReceivedDto, CancellationToken>((message, _) => ingestedMessages.Add(message));
        using var handler = new TestHttpMessageHandler(_ =>
        {
            maxApiRequestCount++;
            return this.Success("{\"is_bot\":true,\"user_id\":201,\"username\":\"test_bot\"}");
        });
        var service = this.CreateService(context, handler, protector, channelServiceMock: channels);
        channels.Setup(channel => channel.EnsureConnectionAsync(
                It.IsAny<EnsureOrganizationChannelConnectionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectionId);
        using var update = JsonDocument.Parse("""
            {
              "update_type": "message_created",
              "update_id": "update-1",
              "timestamp": 1792764000000,
              "message": {
                "timestamp": 1792764000000,
                "sender": { "user_id": 77, "first_name": "Клиент", "username": "client", "is_bot": false },
                "recipient": { "chat_type": "dialog", "user_id": 77 },
                "body": {
                  "mid": "message-1",
                  "text": "Нужна запись",
                  "attachments": [{
                    "type": "image",
                    "payload": { "url": "https://iu.oneme.ru/uploadImage?token=opaque", "token": "opaque" },
                    "filename": "photo.jpg",
                    "size": 1234,
                    "mime_type": "image/jpeg"
                  }]
                }
              }
            }
            """);

        Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, "max-secret", update.RootElement));

        var ingestedMessage = Assert.Single(ingestedMessages);
        Assert.Equal(graph.OrganizationId, ingestedMessage.OrganizationId);
        Assert.Equal(connectionId, ingestedMessage.ConnectionId);
        Assert.Equal("max", ingestedMessage.ProviderCode);
        Assert.Equal("77", ingestedMessage.ExternalConversationId);
        Assert.Equal("77", ingestedMessage.ExternalUserId);
        Assert.Equal("77", ingestedMessage.DeliveryAddress);
        Assert.Equal("update-1", ingestedMessage.ProviderUpdateId);
        Assert.Equal("message-1", ingestedMessage.ProviderMessageId);
        Assert.Equal("Нужна запись", ingestedMessage.Text);
        Assert.Equal(0, maxApiRequestCount);
        var ingestedAttachment = Assert.Single(ingestedMessage.Attachments);
        Assert.Equal("message-1:0", ingestedAttachment.ProviderFileId);
        Assert.Equal(1234L, ingestedAttachment.SizeBytes);

        using var unsupportedUpdate = JsonDocument.Parse("""
            {
              "update_type": "message_created",
              "update_id": "update-2",
              "message": {
                "timestamp": 1792764000000,
                "sender": { "user_id": 77, "is_bot": false },
                "recipient": { "chat_type": "dialog", "user_id": 77 },
                "body": { "mid": "message-2", "attachments": [{ "type": "sticker", "payload": {} }] }
              }
            }
            """);
        Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, "max-secret", unsupportedUpdate.RootElement));
        channels.Verify(channel => channel.IngestMessageAsync(
            It.Is<ChannelMessageReceivedDto>(message => message.ProviderMessageId == "message-2"
                && message.Text != null && message.Text.Contains("sticker", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
        foreach (var malformedBody in new[] { "", ",\"body\":null", ",\"body\":[]" })
        {
            using var malformed = JsonDocument.Parse("{\"update_type\":\"message_created\",\"message\":{"
                + "\"sender\":{\"user_id\":77},\"recipient\":{\"chat_type\":\"dialog\",\"user_id\":77}"
                + malformedBody + "}}");
            Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, "max-secret", malformed.RootElement));
        }
        Assert.Equal(2, ingestedMessages.Count);
    }

    [Fact]
    public async Task BotStartedWithoutPayloadReactivatesOnlyTheExistingVerifiedClientChat()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true);
        var protector = new EphemeralDataProtectionProvider();
        var secret = "opencrm-webhook-secret";
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationMaxBots.SingleAsync();
        var linkedClient = await context.OrganizationClients.SingleAsync(item => item.Id == graph.ClientId);
        linkedClient.MaxNotificationsEnabled = false;
        bot.ImmutableProviderBotId = "201";
        bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect("max-token");
        bot.ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect(secret);
        var connection = new OrganizationChannelConnection
        {
            OrganizationId = graph.OrganizationId,
            ProviderCode = "max",
            ImmutableProviderIdentity = "201",
            IsCurrent = true,
            VerifiedAt = DateTime.UtcNow
        };
        context.OrganizationChannelConnections.Add(connection);
        await context.SaveChangesAsync();

        var channels = new Mock<IOrganizationChannelService>();
        channels.Setup(channel => channel.SetChannelActiveAsync(
                graph.OrganizationId,
                connection.Id,
                "123456",
                true,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Callback(() => Assert.NotNull(context.Database.CurrentTransaction));
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")),
            protector, channelServiceMock: channels);
        using var update = JsonDocument.Parse($$"""
            {
              "update_type": "bot_started",
              "timestamp": 1792764000000,
              "user": { "user_id": 123456 }
            }
            """);

        Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, secret, update.RootElement));

        channels.Verify(channel => channel.SetChannelActiveAsync(
            graph.OrganizationId,
            connection.Id,
            "123456",
            true,
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Once);
        var client = await context.OrganizationClients.SingleAsync(item => item.Id == graph.ClientId);
        Assert.True(client.MaxNotificationsEnabled);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    public async Task BotStartedWithNullOrEmptyPayloadResumesOnlyTheExistingVerifiedClientChat(string payload)
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true);
        var protector = new EphemeralDataProtectionProvider();
        const string secret = "opencrm-webhook-secret";
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationMaxBots.SingleAsync();
        var client = await context.OrganizationClients.SingleAsync(item => item.Id == graph.ClientId);
        client.MaxUserId = 123456;
        client.MaxConnectedAt = DateTimeOffset.FromUnixTimeMilliseconds(1792764000000).UtcDateTime.AddMinutes(-1);
        client.MaxNotificationsEnabled = true;
        bot.ImmutableProviderBotId = "201";
        bot.ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect(secret);
        var linkedClient = await context.OrganizationClients.SingleAsync(item => item.Id == graph.ClientId);
        linkedClient.MaxNotificationsEnabled = false;
        var connection = new OrganizationChannelConnection
        {
            OrganizationId = graph.OrganizationId,
            ProviderCode = "max",
            ImmutableProviderIdentity = "201",
            IsCurrent = true,
            VerifiedAt = DateTime.UtcNow
        };
        context.OrganizationChannelConnections.Add(connection);
        await context.SaveChangesAsync();

        var channels = new Mock<IOrganizationChannelService>();
        channels.Setup(channel => channel.SetChannelActiveAsync(
                graph.OrganizationId,
                connection.Id,
                "123456",
                true,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Callback(() => Assert.NotNull(context.Database.CurrentTransaction));
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")),
            protector, channelServiceMock: channels);
        using var update = JsonDocument.Parse($$"""
            {
              "update_type": "bot_started",
              "timestamp": 1792764000000,
              "payload": {{payload}},
              "user": { "user_id": 123456 }
            }
            """);

        Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, secret, update.RootElement));
        Assert.True(linkedClient.MaxNotificationsEnabled);
        channels.Verify(channel => channel.SetChannelActiveAsync(
            graph.OrganizationId,
            connection.Id,
            "123456",
            true,
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BotStoppedDisablesLegacyClientWhenNoConversationExists()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true);
        var protector = new EphemeralDataProtectionProvider();
        const string secret = "opencrm-webhook-secret";
        await using var context = fixture.CreateContext();
        var client = await context.OrganizationClients.SingleAsync(item => item.Id == graph.ClientId);
        client.MaxConnectedAt = DateTimeOffset.FromUnixTimeMilliseconds(1792764000000).UtcDateTime.AddMinutes(-1);
        var bot = await context.OrganizationMaxBots.SingleAsync();
        bot.ImmutableProviderBotId = "201";
        bot.ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect(secret);
        var connection = new OrganizationChannelConnection
        {
            OrganizationId = graph.OrganizationId,
            ProviderCode = "max",
            ImmutableProviderIdentity = "201",
            IsCurrent = true,
            VerifiedAt = DateTime.UtcNow
        };
        context.OrganizationChannelConnections.Add(connection);
        await context.SaveChangesAsync();

        var channels = new Mock<IOrganizationChannelService>();
        channels.Setup(channel => channel.SetChannelActiveAsync(
                graph.OrganizationId,
                connection.Id,
                "123456",
                false,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .Callback(() => Assert.NotNull(context.Database.CurrentTransaction));
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")),
            protector, channelServiceMock: channels);
        using var update = JsonDocument.Parse("""
            {
              "update_type": "bot_stopped",
              "timestamp": 1792764000000,
              "user": { "user_id": 123456 }
            }
            """);

        Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, secret, update.RootElement));
        Assert.False((await context.OrganizationClients.SingleAsync(client => client.Id == graph.ClientId)).MaxNotificationsEnabled);
    }

    [Fact]
    public async Task StaleBotStartedDoesNotEnableTheClientAgain()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true);
        var protector = new EphemeralDataProtectionProvider();
        const string secret = "opencrm-webhook-secret";
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationMaxBots.SingleAsync();
        bot.ImmutableProviderBotId = "201";
        bot.ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect(secret);
        var client = await context.OrganizationClients.SingleAsync(item => item.Id == graph.ClientId);
        client.MaxNotificationsEnabled = false;
        var connection = new OrganizationChannelConnection
        {
            OrganizationId = graph.OrganizationId,
            ProviderCode = "max",
            ImmutableProviderIdentity = "201",
            IsCurrent = true,
            VerifiedAt = DateTime.UtcNow
        };
        context.OrganizationChannelConnections.Add(connection);
        await context.SaveChangesAsync();

        var channels = new Mock<IOrganizationChannelService>();
        channels.Setup(channel => channel.SetChannelActiveAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                true,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")),
            protector, channelServiceMock: channels);
        using var update = JsonDocument.Parse("""
            {
              "update_type": "bot_started",
              "user": { "user_id": 123456 }
            }
            """);

        Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, secret, update.RootElement));
        Assert.False(client.MaxNotificationsEnabled);
    }

    [Fact]
    public async Task CorruptProtectedChannelTokenIsReportedAsUnavailableCredential()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true);
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationMaxBots.SingleAsync();
        bot.ImmutableProviderBotId = "201";
        bot.ProtectedToken = "corrupt-protected-value";
        var connection = new OrganizationChannelConnection
        {
            OrganizationId = graph.OrganizationId,
            ProviderCode = "max",
            ImmutableProviderIdentity = "201",
            IsCurrent = true,
            VerifiedAt = DateTime.UtcNow
        };
        context.OrganizationChannelConnections.Add(connection);
        await context.SaveChangesAsync();
        var reader = new OrganizationChannelCredentialReader(
            context,
            new EphemeralDataProtectionProvider(),
            NullLogger<OrganizationChannelCredentialReader>.Instance);

        var credential = await reader.GetActiveCredentialAsync(graph.OrganizationId, connection.Id);

        Assert.Null(credential);
    }

    [Fact]
    public async Task WebhookIgnoresMalformedAndUnmatchedUpdatesAfterAuthenticatingMax()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, clientLinkToken: "expired-link");
        var protector = new EphemeralDataProtectionProvider();
        const string secret = "opencrm-webhook-secret";
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationMaxBots.SingleAsync();
        bot.ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect(secret);
        var client = await context.OrganizationClients.SingleAsync();
        client.MaxLinkExpiresAt = DateTime.UtcNow.AddDays(-1);
        await context.SaveChangesAsync();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), protector);

        string[] updates =
        [
            "[]",
            "{}",
            "{\"update_type\":\"bot_started\"}",
            "{\"update_type\":\"bot_started\",\"user\":null}",
            "{\"update_type\":\"bot_started\",\"user\":{}}",
            "{\"update_type\":\"bot_started\",\"user\":{\"user_id\":\"123456\"}}",
            "{\"update_type\":123,\"user\":{\"user_id\":123456}}",
            "{\"update_type\":\"bot_started\",\"user\":{\"user_id\":123456}}",
            "{\"update_type\":\"bot_started\",\"payload\":\"unknown\",\"user\":{\"user_id\":123456}}",
            "{\"update_type\":\"bot_stopped\",\"timestamp\":-1,\"user\":{\"user_id\":123456}}"
        ];

        foreach (var updateJson in updates)
        {
            using var update = JsonDocument.Parse(updateJson);
            Assert.True(await service.ProcessWebhookAsync(graph.OrganizationId, secret, update.RootElement));
        }

        using var emptyUpdate = JsonDocument.Parse("{}");
        Assert.False(await service.ProcessWebhookAsync(graph.OrganizationId, null, emptyUpdate.RootElement));
        Assert.False(await service.ProcessWebhookAsync(Guid.NewGuid(), secret, emptyUpdate.RootElement));
        Assert.False((await context.OrganizationClients.SingleAsync()).MaxNotificationsEnabled);
    }

    [Fact]
    public async Task CredentialIsReturnedOnlyForTheCurrentEligibleNotification()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, botEnabled: true, notification: true);
        var protector = new EphemeralDataProtectionProvider();
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationMaxBots.SingleAsync();
        bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect("bot-token");
        await context.SaveChangesAsync();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), protector);

        Assert.Equal("bot-token", await service.GetBotCredentialAsync(graph.OrganizationId, graph.NotificationKey!, CancellationToken.None));
        Assert.Null(await service.GetBotCredentialAsync(graph.OrganizationId, " ", CancellationToken.None));
        Assert.Null(await service.GetBotCredentialAsync(graph.OrganizationId, "max:unknown", CancellationToken.None));
        Assert.Null(await service.GetBotCredentialAsync(Guid.NewGuid(), graph.NotificationKey!, CancellationToken.None));
    }

    [Fact]
    public async Task CredentialIsNotReturnedWhenVehicleIsDeletedDuringLookup()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, botEnabled: true, notification: true);
        var protector = new EphemeralDataProtectionProvider();
        await using (var setup = fixture.CreateContext())
        {
            var bot = await setup.OrganizationMaxBots.SingleAsync();
            bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect("bot-token");
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
            graph.OrganizationId, graph.NotificationKey!, CancellationToken.None);

        Assert.True(deletion.HasDeleted);
        Assert.Null(credential);
    }

    [Theory]
    [InlineData("client-deleted")]
    [InlineData("client-missing")]
    [InlineData("notifications-disabled")]
    [InlineData("user-changed")]
    [InlineData("vehicle-without-organization")]
    [InlineData("vehicle-client-mismatch")]
    [InlineData("maintenance-cancelled")]
    [InlineData("booking-changed-cancelled")]
    [InlineData("cancellation-not-cancelled")]
    [InlineData("maintenance-missing")]
    [InlineData("ready-not-ready")]
    [InlineData("ready-handed-over")]
    [InlineData("ready-disabled")]
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
        var bot = await context.OrganizationMaxBots.SingleAsync();
        var organization = await context.Organizations.SingleAsync();
        var notification = await context.MaxClientNotifications.SingleAsync();
        bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect("bot-token");

        switch (scenario)
        {
            case "client-deleted":
                client.IsDeleted = true;
                break;
            case "client-missing":
                notification.ClientId = Guid.NewGuid();
                break;
            case "notifications-disabled":
                client.MaxNotificationsEnabled = false;
                break;
            case "user-changed":
                client.MaxUserId = 123457;
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
            case "later-notification":
                context.MaxClientNotifications.Add(new MaxClientNotification
                {
                    OrganizationId = graph.OrganizationId,
                    ClientId = graph.ClientId,
                    MaxUserId = 123456,
                    MaintenanceId = maintenance.Id,
                    EventCode = "booking_changed",
                    NotificationKey = $"max:{Guid.NewGuid():N}",
                    Body = "Новое уведомление",
                    Status = "queued",
                    NextAttemptAt = DateTime.UtcNow
                });
                break;
            case "bot-disabled":
                bot.IsEnabled = false;
                break;
            case "bot-missing":
                context.OrganizationMaxBots.Remove(bot);
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
            graph.OrganizationId, graph.NotificationKey!, CancellationToken.None));
    }

    [Fact]
    public async Task ReadyAndCancellationNotificationsRequireTheirMatchingMaintenanceState()
    {
        var graph = await this.CreateOrganizationAsync(includeBot: true, botEnabled: true, notification: true);
        var protector = new EphemeralDataProtectionProvider();
        await using var context = fixture.CreateContext();
        var bot = await context.OrganizationMaxBots.SingleAsync();
        bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect("bot-token");
        var maintenance = await context.Maintenance.SingleAsync();
        var notification = await context.MaxClientNotifications.SingleAsync();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), protector);

        notification.EventCode = "ready";
        maintenance.Status = MaintenanceStatus.Completed;
        maintenance.ReadyForHandoverAt = DateTime.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();
        Assert.Equal("bot-token", await service.GetBotCredentialAsync(
            graph.OrganizationId, graph.NotificationKey!, CancellationToken.None));

        maintenance.HandedOverAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        Assert.Null(await service.GetBotCredentialAsync(graph.OrganizationId, graph.NotificationKey!, CancellationToken.None));

        notification.EventCode = "booking_cancelled";
        maintenance.Status = MaintenanceStatus.Cancelled;
        maintenance.ReadyForHandoverAt = null;
        maintenance.HandedOverAt = null;
        await context.SaveChangesAsync();
        Assert.Equal("bot-token", await service.GetBotCredentialAsync(
            graph.OrganizationId, graph.NotificationKey!, CancellationToken.None));
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
        var bot = await context.OrganizationMaxBots.SingleAsync();
        bot.ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect("bot-token");
        await context.SaveChangesAsync();
        var service = this.CreateService(context, new TestHttpMessageHandler(_ => this.Success("{}")), protector);

        Assert.Null(await service.GetBotCredentialAsync(graph.OrganizationId, graph.NotificationKey!, CancellationToken.None));
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
            MaxUserId = 123456,
            MaxConnectedAt = DateTime.UtcNow.AddMinutes(-1),
            MaxNotificationsEnabled = true,
            MaxLinkTokenHash = clientLinkToken is null ? null : HashToken(clientLinkToken),
            MaxLinkExpiresAt = clientLinkToken is null ? null : DateTime.UtcNow.AddDays(1)
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
            context.OrganizationMaxBots.Add(new OrganizationMaxBot
            {
                Organization = organization,
                Username = username,
                ProtectedToken = "protected-token",
                ProtectedWebhookSecret = "protected-secret",
                IsEnabled = botEnabled
            });
        }

        string? notificationKey = null;
        if (notification)
        {
            notificationKey = $"max:{Guid.NewGuid():N}";
            context.MaxClientNotifications.Add(new MaxClientNotification
            {
                OrganizationId = organization.Id,
                ClientId = client.Id,
                MaxUserId = client.MaxUserId!.Value,
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

    private OrganizationMaxService CreateService(
        OpenCRMContext context,
        HttpMessageHandler handler,
        IDataProtectionProvider? dataProtectionProvider = null,
        Dictionary<string, string?>? configuration = null,
        Mock<IOrganizationChannelService>? channelServiceMock = null,
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

        var values = new Dictionary<string, string?> { ["AppSettings:BaseUrl"] = "https://opencrm.example" };
        if (configuration is not null)
        {
            foreach (var (key, value) in configuration)
            {
                values[key] = value;
            }
        }

        var channelService = channelServiceMock ?? new Mock<IOrganizationChannelService>();
        if (channelServiceMock is null)
        {
            channelService
                .Setup(service => service.EnsureConnectionAsync(
                    It.IsAny<EnsureOrganizationChannelConnectionDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Guid.NewGuid());
            channelService
                .Setup(service => service.BindClientAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(),
                    It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            channelService
                .Setup(service => service.SetChannelActiveAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        return new OrganizationMaxService(
            context,
            dataProtectionProvider ?? new EphemeralDataProtectionProvider(),
            new TestHttpClientFactory(handler),
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            subscriptionService.Object,
            channelService.Object);
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
            BaseAddress = new Uri("https://platform-api2.max.ru/")
        };
    }
}
