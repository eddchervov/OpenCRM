using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Notifications.Max;
using OpenCRM.Services.Interfaces.Organizations;
using System.Text.Json;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

public sealed class VisitReminderDeliveryTests(ClientNotificationDatabaseFixture fixture)
    : IClassFixture<ClientNotificationDatabaseFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CurrentQueuedReminderReturnsProtectedBotCredential()
    {
        var protector = new EphemeralDataProtectionProvider();
        await using var context = fixture.CreateContext();
        var graph = await this.CreateReminderAsync(context, protector);
        var service = this.CreateService(context, protector);

        var credential = await service.GetBotCredentialAsync(
            graph.Organization.Id, graph.Notification.NotificationKey, CancellationToken.None);

        Assert.Equal("synthetic-max-token", credential);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryOnlyShowsReminderBodyWithDirectoryPermissions(bool canReadDetails)
    {
        var protector = new EphemeralDataProtectionProvider();
        await using var context = fixture.CreateContext();
        var graph = await this.CreateReminderAsync(context, protector);
        graph.Notification.Body = "Скрытые данные клиента и автомобиля";
        await context.SaveChangesAsync();
        var service = this.CreateService(context, protector, canReadDetails);

        var result = await service.GetMaintenanceNotificationsAsync(graph.Organization.OwnerUserId, graph.Organization.Id, graph.Maintenance.Id);
        var body = JsonSerializer.SerializeToElement(result).EnumerateArray().Single().GetProperty("Body").GetString();

        Assert.Equal(canReadDetails ? "Скрытые данные клиента и автомобиля" : "Текст недоступен без прав просмотра клиентов и автомобилей.", body);
    }

    [Theory]
    [InlineData("booking-moved")]
    [InlineData("booking-cancelled")]
    [InlineData("individual-disabled")]
    [InlineData("organization-disabled")]
    [InlineData("lead-time-changed")]
    [InlineData("channel-changed")]
    [InlineData("foreign-client")]
    [InlineData("vehicle-soft-deleted")]
    public async Task StaleReminderDoesNotReturnBotCredential(string scenario)
    {
        var protector = new EphemeralDataProtectionProvider();
        await using var context = fixture.CreateContext();
        var graph = await this.CreateReminderAsync(context, protector);

        switch (scenario)
        {
            case "booking-moved":
                graph.Maintenance.PlannedStartTime = graph.Maintenance.PlannedStartTime!.Value.Add(TimeSpan.FromMinutes(15));
                break;
            case "booking-cancelled":
                graph.Maintenance.Status = MaintenanceStatus.Cancelled;
                break;
            case "individual-disabled":
                graph.Maintenance.VisitReminderEnabled = false;
                break;
            case "organization-disabled":
                graph.Organization.VisitReminderHoursBefore = null;
                break;
            case "lead-time-changed":
                graph.Organization.VisitReminderHoursBefore = 2;
                break;
            case "channel-changed":
                graph.Organization.VisitReminderChannelCode = "telegram";
                break;
            case "foreign-client":
                graph.Notification.ClientId = Guid.NewGuid();
                break;
            case "vehicle-soft-deleted":
                graph.Vehicle.IsDeleted = true;
                break;
        }

        await context.SaveChangesAsync();
        var service = this.CreateService(context, protector);

        Assert.Null(await service.GetBotCredentialAsync(
            graph.Organization.Id, graph.Notification.NotificationKey, CancellationToken.None));
    }

    [Fact]
    public async Task NewerReminderDoesNotSuppressCurrentBookingCreatedNotification()
    {
        var protector = new EphemeralDataProtectionProvider();
        await using var context = fixture.CreateContext();
        var graph = await this.CreateReminderAsync(context, protector, eventCode: "booking_created");
        context.MaxClientNotifications.Add(new MaxClientNotification
        {
            OrganizationId = graph.Organization.Id,
            ClientId = graph.Client.Id,
            MaxUserId = graph.Client.MaxUserId!.Value,
            MaintenanceId = graph.Maintenance.Id,
            VisitStartsAtUtc = graph.StartsAtUtc,
            EventCode = MaintenanceVisitReminderRules.EventCode,
            NotificationKey = MaintenanceVisitReminderRules.GetNotificationKey(
                graph.Maintenance.Id, graph.StartsAtUtc, graph.Organization.VisitReminderHoursBefore!.Value, "max"),
            Body = "Плановое напоминание.",
            Status = "queued",
            NextAttemptAt = DateTime.UtcNow.AddMinutes(-1)
        });
        await context.SaveChangesAsync();
        var service = this.CreateService(context, protector);

        var credential = await service.GetBotCredentialAsync(
            graph.Organization.Id, graph.Notification.NotificationKey, CancellationToken.None);

        Assert.Equal("synthetic-max-token", credential);
    }

    private async Task<ReminderGraph> CreateReminderAsync(
        OpenCRMContext context,
        IDataProtectionProvider protector,
        int hoursBefore = 24,
        string eventCode = MaintenanceVisitReminderRules.EventCode)
    {
        var startsAtUtc = DateTime.UtcNow.AddMinutes(90);
        startsAtUtc = new DateTime(startsAtUtc.Year, startsAtUtc.Month, startsAtUtc.Day,
            startsAtUtc.Hour, startsAtUtc.Minute, 0, DateTimeKind.Utc);
        var user = new ApplicationUser
        {
            UserName = $"visit-reminder-{Guid.NewGuid():N}",
            Email = $"visit-reminder-{Guid.NewGuid():N}@example.test"
        };
        var organization = new Organization
        {
            Name = "Тестовая организация",
            OwnerUser = user,
            ShiftTimeZoneId = "UTC",
            VisitReminderHoursBefore = hoursBefore,
            VisitReminderChannelCode = "max"
        };
        var client = new OrganizationClient
        {
            Organization = organization,
            Name = "Клиент",
            MaxUserId = 987654,
            MaxNotificationsEnabled = true
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
            Date = startsAtUtc.Date,
            PlannedDate = startsAtUtc.Date,
            PlannedStartTime = startsAtUtc.TimeOfDay
        };
        var bot = new OrganizationMaxBot
        {
            Organization = organization,
            Username = $"max-{Guid.NewGuid():N}",
            ProtectedToken = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect("synthetic-max-token"),
            ProtectedWebhookSecret = protector.CreateProtector("OpenCRM.OrganizationMaxBot.v1").Protect("synthetic-secret"),
            IsEnabled = true,
            NotifyBooking = true
        };
        var notification = new MaxClientNotification
        {
            OrganizationId = organization.Id,
            ClientId = client.Id,
            MaxUserId = client.MaxUserId.Value,
            MaintenanceId = maintenance.Id,
            VisitStartsAtUtc = startsAtUtc,
            EventCode = eventCode,
            NotificationKey = eventCode == MaintenanceVisitReminderRules.EventCode
                ? MaintenanceVisitReminderRules.GetNotificationKey(maintenance.Id, startsAtUtc, hoursBefore, "max")
                : $"max:{Guid.NewGuid():N}",
            Body = "Напоминание о записи.",
            Status = "queued",
            NextAttemptAt = DateTime.UtcNow.AddMinutes(-1)
        };
        context.Maintenance.Add(maintenance);
        context.OrganizationMaxBots.Add(bot);
        context.MaxClientNotifications.Add(notification);
        await context.SaveChangesAsync();

        return new ReminderGraph(organization, client, vehicle, maintenance, notification, startsAtUtc);
    }

    private OrganizationMaxService CreateService(OpenCRMContext context, IDataProtectionProvider protector, bool canReadDetails = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MaxNotifications:Enabled"] = "true" })
            .Build();
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(item => item.HasPermissionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(canReadDetails);

        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(item => item.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), Helpers.FeatureCodes.ClientMessaging))
            .ReturnsAsync(true);

        return new OrganizationMaxService(
            context,
            protector,
            new NoRequestHttpClientFactory(),
            configuration,
            subscriptions.Object,
            new Mock<IOrganizationChannelService>().Object,
            permissions.Object);
    }

    private sealed record ReminderGraph(
        Organization Organization,
        OrganizationClient Client,
        Vehicle Vehicle,
        MaintenanceEntity Maintenance,
        MaxClientNotification Notification,
        DateTime StartsAtUtc);

    private sealed class NoRequestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new NoRequestHandler());
    }

    private sealed class NoRequestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Для чтения учётных данных внешний HTTP-запрос не ожидается.");
        }
    }
}
