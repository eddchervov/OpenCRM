using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceNotificationOutboxTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task QueueChangeCreatesMaxAndTelegramHistoryOnceForEligibleConnectedClient()
    {
        await using var context = fixture.CreateContext();
        var (organization, client, maintenance, _, _) = await CreateNotificationGraphAsync(context);
        var previous = MaintenanceSnapshot.From(maintenance);
        maintenance.PlannedStartTime = TimeSpan.FromHours(10);
        await context.SaveChangesAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TelegramOrganizationNotifications:Enabled"] = "true"
            })
            .Build();
        var outbox = new MaintenanceNotificationOutbox(context, configuration);

        await outbox.QueueMaxChangeAsync(organization.Id, maintenance, previous);
        await outbox.QueueTelegramChangeAsync(organization.Id, maintenance, previous);
        await outbox.QueueMaxChangeAsync(organization.Id, maintenance, previous);
        await outbox.QueueTelegramChangeAsync(organization.Id, maintenance, previous);

        var maxNotification = await context.MaxClientNotifications
            .SingleAsync(item => item.MaintenanceId == maintenance.Id);
        Assert.Equal(client.Id, maxNotification.ClientId);
        Assert.Equal(maintenance.Id, maxNotification.MaintenanceId);
        Assert.Equal("booking_changed", maxNotification.EventCode);
        Assert.Equal($"max:{maintenance.Id:N}:booking_changed:{maintenance.Version}", maxNotification.NotificationKey);
        Assert.Equal("Очередь: время записи на автомобиль А123АА изменено на 12.10.2026 в 10:00.", maxNotification.Body);
        Assert.Equal("queued", maxNotification.Status);
        Assert.Equal(0, maxNotification.AttemptCount);

        var telegramNotification = await context.TelegramClientNotifications
            .SingleAsync(item => item.MaintenanceId == maintenance.Id);
        Assert.Equal(client.Id, telegramNotification.ClientId);
        Assert.Equal(456L, telegramNotification.ChatId);
        Assert.Equal("booking_changed", telegramNotification.EventCode);
        Assert.Equal($"telegram-client:{maintenance.Id:N}:booking_changed:{maintenance.Version}", telegramNotification.NotificationKey);
        Assert.Equal(maxNotification.Body, telegramNotification.Body);
        Assert.Equal("queued", telegramNotification.Status);
        Assert.Equal(0, telegramNotification.AttemptCount);
    }

    [Fact]
    public async Task QueueNotificationsHonorsChannelConfigurationClientConnectionAndBotEventPreferences()
    {
        await using var context = fixture.CreateContext();
        var (organization, client, maintenance, maxBot, telegramBot) = await CreateNotificationGraphAsync(context);
        var snapshot = MaintenanceSnapshot.From(maintenance);

        var disabledTelegram = new MaintenanceNotificationOutbox(context);
        maintenance.PlannedStartTime = TimeSpan.FromHours(11);
        await disabledTelegram.QueueTelegramChangeAsync(organization.Id, maintenance, snapshot);
        Assert.Empty(await context.TelegramClientNotifications
            .Where(item => item.MaintenanceId == maintenance.Id).ToListAsync());

        var enabledConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TelegramOrganizationNotifications:Enabled"] = "true"
            })
            .Build();
        var outbox = new MaintenanceNotificationOutbox(context, enabledConfiguration);
        maxBot.NotifyReady = false;
        telegramBot.NotifyReady = false;
        var readySnapshot = MaintenanceSnapshot.From(maintenance);
        maintenance.Status = MaintenanceStatus.Completed;
        maintenance.ReadyForHandoverAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        await outbox.QueueMaxChangeAsync(organization.Id, maintenance, readySnapshot);
        await outbox.QueueTelegramChangeAsync(organization.Id, maintenance, readySnapshot);

        Assert.Empty(await context.MaxClientNotifications
            .Where(item => item.MaintenanceId == maintenance.Id).ToListAsync());
        Assert.Empty(await context.TelegramClientNotifications
            .Where(item => item.MaintenanceId == maintenance.Id).ToListAsync());

        maxBot.NotifyReady = true;
        telegramBot.NotifyReady = true;
        client.MaxNotificationsEnabled = false;
        client.TelegramNotificationsEnabled = false;
        await context.SaveChangesAsync();

        await outbox.QueueMaxChangeAsync(organization.Id, maintenance, readySnapshot);
        await outbox.QueueTelegramChangeAsync(organization.Id, maintenance, readySnapshot);

        Assert.Empty(await context.MaxClientNotifications
            .Where(item => item.MaintenanceId == maintenance.Id).ToListAsync());
        Assert.Empty(await context.TelegramClientNotifications
            .Where(item => item.MaintenanceId == maintenance.Id).ToListAsync());
    }

    [Fact]
    public async Task QueueReadyNotificationsUsesReadyPreferencesAndStoresEventHistory()
    {
        await using var context = fixture.CreateContext();
        var (organization, client, maintenance, maxBot, telegramBot) = await CreateNotificationGraphAsync(context);
        var previous = MaintenanceSnapshot.From(maintenance);
        maxBot.NotifyBooking = false;
        telegramBot.NotifyBooking = false;
        maintenance.Status = MaintenanceStatus.Completed;
        maintenance.ReadyForHandoverAt = new DateTime(2026, 10, 12, 15, 30, 0, DateTimeKind.Utc);
        await context.SaveChangesAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TelegramOrganizationNotifications:Enabled"] = "true"
            })
            .Build();
        var outbox = new MaintenanceNotificationOutbox(context, configuration);

        await outbox.QueueMaxChangeAsync(organization.Id, maintenance, previous);
        await outbox.QueueTelegramChangeAsync(organization.Id, maintenance, previous);

        var maxNotification = await context.MaxClientNotifications
            .SingleAsync(item => item.MaintenanceId == maintenance.Id);
        Assert.Equal(client.Id, maxNotification.ClientId);
        Assert.Equal("ready", maxNotification.EventCode);
        Assert.Equal($"max:{maintenance.Id:N}:ready:{maintenance.Version}", maxNotification.NotificationKey);
        Assert.Equal("Очередь: автомобиль А123АА готов к выдаче.", maxNotification.Body);
        Assert.Equal("queued", maxNotification.Status);

        var telegramNotification = await context.TelegramClientNotifications
            .SingleAsync(item => item.MaintenanceId == maintenance.Id);
        Assert.Equal(client.Id, telegramNotification.ClientId);
        Assert.Equal("ready", telegramNotification.EventCode);
        Assert.Equal($"telegram-client:{maintenance.Id:N}:ready:{maintenance.Version}", telegramNotification.NotificationKey);
        Assert.Equal(maxNotification.Body, telegramNotification.Body);
        Assert.Equal("queued", telegramNotification.Status);
    }

    [Fact]
    public async Task QueueChangeSkipsSoftDeletedClientsAndUnmatchedOrganizations()
    {
        await using var context = fixture.CreateContext();
        var (organization, client, maintenance, _, _) = await CreateNotificationGraphAsync(context);
        var previous = MaintenanceSnapshot.From(maintenance);
        maintenance.PlannedStartTime = TimeSpan.FromHours(12);
        client.IsDeleted = true;
        await context.SaveChangesAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TelegramOrganizationNotifications:Enabled"] = "true"
            })
            .Build();
        var outbox = new MaintenanceNotificationOutbox(context, configuration);

        await outbox.QueueMaxChangeAsync(organization.Id, maintenance, previous);
        await outbox.QueueTelegramChangeAsync(organization.Id, maintenance, previous);
        await outbox.QueueMaxChangeAsync(Guid.NewGuid(), maintenance, previous);
        await outbox.QueueTelegramChangeAsync(Guid.NewGuid(), maintenance, previous);

        Assert.Empty(await context.MaxClientNotifications
            .Where(item => item.MaintenanceId == maintenance.Id).ToListAsync());
        Assert.Empty(await context.TelegramClientNotifications
            .Where(item => item.MaintenanceId == maintenance.Id).ToListAsync());
    }

    private static async Task<(Organization Organization, OrganizationClient Client, MaintenanceEntity Maintenance,
        OrganizationMaxBot MaxBot, OrganizationTelegramBot TelegramBot)> CreateNotificationGraphAsync(OpenCRMContext context)
    {
        var user = new ApplicationUser { UserName = $"notification-{Guid.NewGuid():N}", Email = "client@example.test" };
        var organization = new Organization { Name = "Очередь" , OwnerUser = user };
        var client = new OrganizationClient
        {
            Organization = organization,
            Name = "Клиент",
            MaxUserId = 123,
            MaxNotificationsEnabled = true,
            TelegramChatId = 456,
            TelegramNotificationsEnabled = true
        };
        var vehicle = new Vehicle
        {
            User = user,
            Organization = organization,
            OrganizationClient = client,
            RegistrationPlate = "А123АА"
        };
        var maintenance = new MaintenanceEntity
        {
            User = user,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Planned,
            Date = new DateTime(2026, 10, 12),
            PlannedDate = new DateTime(2026, 10, 12),
            PlannedStartTime = TimeSpan.FromHours(9)
        };
        var maxBot = new OrganizationMaxBot
        {
            Organization = organization,
            Username = $"max-{Guid.NewGuid():N}",
            ProtectedToken = "protected-token",
            ProtectedWebhookSecret = "protected-secret",
            IsEnabled = true,
            NotifyBooking = true,
            NotifyReady = true
        };
        var telegramBot = new OrganizationTelegramBot
        {
            Organization = organization,
            Username = $"telegram-{Guid.NewGuid():N}",
            ProtectedToken = "protected-token",
            ProtectedWebhookSecret = "protected-secret",
            IsEnabled = true,
            NotifyBooking = true,
            NotifyReady = true
        };
        context.Maintenance.Add(maintenance);
        context.OrganizationMaxBots.Add(maxBot);
        context.OrganizationTelegramBots.Add(telegramBot);
        await context.SaveChangesAsync();

        return (organization, client, maintenance, maxBot, telegramBot);
    }
}
