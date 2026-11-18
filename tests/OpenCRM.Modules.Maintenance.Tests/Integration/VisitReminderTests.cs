using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Reminders;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class VisitReminderTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    private static readonly DateTime Now = new(2026, 10, 24, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task DueReminderUsesLocalTimeAndQueuesOnceForPreferredChannel()
    {
        await using var context = fixture.CreateContext();
        var work = VisitReminderTests.CreateWork();
        await VisitReminderTests.SaveWorkAsync(context, work);
        var service = VisitReminderTests.CreateService(context);
        var scheduled = await service.GetAsync(work.User.Id, work.Vehicle.OrganizationId!.Value, work.Id);
        Assert.Equal("scheduled", scheduled.StatusCode);
        Assert.Equal("scheduled", scheduled.StatusDetailCode);
        Assert.Equal("Asia/Novosibirsk", scheduled.TimeZoneId);
        Assert.Equal(Now, scheduled.ScheduledAtUtc);
        await service.SendDueAsync(CancellationToken.None);
        await service.SendDueAsync(CancellationToken.None);

        var queued = await context.TelegramClientNotifications.SingleAsync(item => item.MaintenanceId == work.Id);
        Assert.Equal(Now.AddDays(1), queued.VisitStartsAtUtc);
        Assert.Contains("25.10.2026 в 10:00", queued.Body);
        Assert.Contains("А123АА154", queued.Body);
        Assert.Equal("booking_reminder", queued.EventCode);
        Assert.False(await context.MaxClientNotifications.AnyAsync(item => item.MaintenanceId == work.Id));
        var preview = await service.GetAsync(work.User.Id, work.Vehicle.OrganizationId!.Value, work.Id);
        Assert.Equal("queued", preview.StatusCode);
        Assert.Equal("queued", preview.StatusDetailCode);
    }

    [Fact]
    public async Task MoveCreatesNewScheduleWhileCancelledAndDisabledDoNotQueue()
    {
        await using var context = fixture.CreateContext();
        var work = VisitReminderTests.CreateWork();
        await VisitReminderTests.SaveWorkAsync(context, work);
        var service = VisitReminderTests.CreateService(context);
        await service.SendDueAsync(CancellationToken.None);
        var saved = await context.Maintenance.SingleAsync(item => item.Id == work.Id);
        saved.PlannedStartTime = TimeSpan.FromHours(9);
        await context.SaveChangesAsync();
        await service.SendDueAsync(CancellationToken.None);
        Assert.Equal(2, await context.TelegramClientNotifications.CountAsync(item => item.MaintenanceId == work.Id));
        saved = await context.Maintenance.SingleAsync(item => item.Id == work.Id);
        saved.Status = MaintenanceStatus.Cancelled;
        saved.PlannedStartTime = TimeSpan.FromHours(8);
        await context.SaveChangesAsync();
        await service.SendDueAsync(CancellationToken.None);
        Assert.Equal(2, await context.TelegramClientNotifications.CountAsync(item => item.MaintenanceId == work.Id));
    }

    [Fact]
    public async Task ToggleChecksVersionAndTenantAndWritesHistory()
    {
        await using var context = fixture.CreateContext();
        var work = VisitReminderTests.CreateWork();
        await VisitReminderTests.SaveWorkAsync(context, work);
        var service = VisitReminderTests.CreateService(context);
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var version = work.Version;
        var response = await service.SetEnabledAsync(work.User.Id, organizationId, work.Id, false, version);
        Assert.False(response.Enabled);
        Assert.Equal("disabled_record", response.StatusDetailCode);
        Assert.NotEqual(version, response.Version);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.SetEnabledAsync(work.User.Id, organizationId, work.Id, true, version));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(work.User.Id, Guid.NewGuid(), work.Id));
        Assert.True(await context.MaintenanceHistoryEvents.AnyAsync(item => item.MaintenanceId == work.Id && item.EventType == "visit_reminder_changed"));
        await service.SendDueAsync(CancellationToken.None);
        Assert.False(await context.TelegramClientNotifications.AnyAsync(item => item.MaintenanceId == work.Id));
    }

    [Fact]
    public async Task SettingsRequireRightsAndTimeZoneAndRejectUnknownTemplate()
    {
        await using var context = fixture.CreateContext();
        var work = VisitReminderTests.CreateWork();
        work.Vehicle.Organization!.ShiftTimeZoneId = null;
        await VisitReminderTests.SaveWorkAsync(context, work);
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var service = VisitReminderTests.CreateService(context);
        var settings = new VisitReminderSettingsDto { HoursBefore = 24, Template = MaintenanceVisitReminderRules.DefaultTemplate };
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveSettingsAsync(work.User.Id, organizationId, settings));
        settings.HoursBefore = null;
        await service.SaveSettingsAsync(work.User.Id, organizationId, settings);
        var disabled = await service.GetAsync(work.User.Id, organizationId, work.Id);
        Assert.Equal("disabled_organization", disabled.StatusDetailCode);
        settings.Template = "{password}";
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveSettingsAsync(work.User.Id, organizationId, settings));
        var denied = new Mock<IOrganizationSubscriptionService>();
        denied.Setup(item => item.HasFeatureAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), Helpers.FeatureCodes.ClientMessaging))
            .ReturnsAsync(true);
        denied.Setup(item => item.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ThrowsAsync(new UnauthorizedAccessException());
        settings.Template = MaintenanceVisitReminderRules.DefaultTemplate;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => VisitReminderTests.CreateService(context, denied.Object)
            .SaveSettingsAsync(work.User.Id, organizationId, settings));
    }

    [Fact]
    public async Task FreePlanCannotEnableAutomaticRemindersButCanTurnThemOff()
    {
        await using var context = fixture.CreateContext();
        var work = VisitReminderTests.CreateWork();
        await VisitReminderTests.SaveWorkAsync(context, work);
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var freeSubscriptions = new Mock<IOrganizationSubscriptionService>();
        freeSubscriptions.Setup(item => item.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), Helpers.FeatureCodes.ClientMessaging))
            .ReturnsAsync(false);
        freeSubscriptions.Setup(item => item.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var freeService = VisitReminderTests.CreateService(context, freeSubscriptions.Object);
        var settings = new VisitReminderSettingsDto
        {
            HoursBefore = 24,
            ChannelCode = "telegram",
            Template = MaintenanceVisitReminderRules.DefaultTemplate
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            freeService.SaveSettingsAsync(work.User.Id, organizationId, settings));
        settings.HoursBefore = null;
        await freeService.SaveSettingsAsync(work.User.Id, organizationId, settings);

        Assert.Contains("PRO", exception.Message, StringComparison.Ordinal);
        Assert.Null((await context.Organizations.SingleAsync(item => item.Id == organizationId)).VisitReminderHoursBefore);
    }

    [Fact]
    public async Task DoesNotQueueBeforeDueAfterVisitOrWithoutConnectedChannel()
    {
        await using var context = fixture.CreateContext();
        var work = VisitReminderTests.CreateWork();
        work.PlannedDate = work.PlannedDate!.Value.AddDays(1);
        await VisitReminderTests.SaveWorkAsync(context, work);
        var service = VisitReminderTests.CreateService(context);
        await service.SendDueAsync(CancellationToken.None);
        Assert.False(await context.TelegramClientNotifications.AnyAsync(item => item.MaintenanceId == work.Id));
        var saved = await context.Maintenance.Include(item => item.Vehicle).ThenInclude(item => item.OrganizationClient)
            .SingleAsync(item => item.Id == work.Id);
        saved.PlannedDate = new DateTime(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc);
        await context.SaveChangesAsync();
        await service.SendDueAsync(CancellationToken.None);
        Assert.False(await context.TelegramClientNotifications.AnyAsync(item => item.MaintenanceId == work.Id));
        saved = await context.Maintenance.Include(item => item.Vehicle).ThenInclude(item => item.OrganizationClient)
            .SingleAsync(item => item.Id == work.Id);
        saved.PlannedDate = saved.PlannedDate!.Value.AddDays(1);
        saved.Vehicle.OrganizationClient!.TelegramNotificationsEnabled = false;
        await context.SaveChangesAsync();
        await service.SendDueAsync(CancellationToken.None);
        Assert.False(await context.TelegramClientNotifications.AnyAsync(item => item.MaintenanceId == work.Id));
    }

    [Fact]
    public async Task PreviewDoesNotExposeClientAndVehicleWithoutDirectoryPermissions()
    {
        await using var context = fixture.CreateContext();
        var work = VisitReminderTests.CreateWork();
        await VisitReminderTests.SaveWorkAsync(context, work);
        var service = VisitReminderTests.CreateService(context);

        var response = await service.GetAsync(work.User.Id, work.Vehicle.OrganizationId!.Value, work.Id);

        Assert.Empty(response.Preview);
        Assert.Equal("scheduled", response.StatusCode);
    }

    private static MaintenanceVisitReminderService CreateService(OpenCRMContext context, IOrganizationSubscriptionService? subscriptionService = null)
    {
        var clock = Mock.Of<ISystemClock>(item => item.UtcNow == VisitReminderTests.Now);
        if (subscriptionService == null)
        {
            var paidSubscriptions = new Mock<IOrganizationSubscriptionService>();
            paidSubscriptions.Setup(item => item.HasFeatureAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), Helpers.FeatureCodes.ClientMessaging))
                .ReturnsAsync(true);
            subscriptionService = paidSubscriptions.Object;
        }

        var subscriptions = subscriptionService;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TelegramOrganizationNotifications:Enabled"] = "true", ["MaxNotifications:Enabled"] = "true"
        }).Build();
        var repository = new MaintenanceRepository(context);

        return new MaintenanceVisitReminderService(context, clock, configuration, subscriptions, Mock.Of<IOrganizationPermissionService>(),
            new MaintenanceAccess(subscriptions), new MaintenanceOrderLoader(repository, context),
            new MaintenanceHistoryWriter(clock, context), new MaintenanceTransaction(context));
    }

    [Fact]
    public async Task ChangingLeadTimeAfterProviderAcceptedDoesNotSendAnotherReminder()
    {
        await using var context = fixture.CreateContext();
        var work = VisitReminderTests.CreateWork();
        work.PlannedDate = VisitReminderTests.Now.Date;
        work.PlannedStartTime = TimeSpan.FromHours(11);
        await VisitReminderTests.SaveWorkAsync(context, work);
        var service = VisitReminderTests.CreateService(context);
        await service.SendDueAsync(CancellationToken.None);
        var queued = await context.TelegramClientNotifications.SingleAsync(item => item.MaintenanceId == work.Id);
        queued.Status = "sent";
        var organization = await context.Organizations.SingleAsync(item => item.Id == work.Vehicle.OrganizationId);
        organization.VisitReminderHoursBefore = 2;
        await context.SaveChangesAsync();
        await service.SendDueAsync(CancellationToken.None);

        Assert.Equal(1, await context.TelegramClientNotifications.CountAsync(item => item.MaintenanceId == work.Id));
        var preview = await service.GetAsync(work.User.Id, work.Vehicle.OrganizationId!.Value, work.Id);
        Assert.Equal("sent", preview.StatusCode);
    }

    private static MaintenanceEntity CreateWork()
    {
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Детейлинг QA", OwnerUser = user, ShiftTimeZoneId = "Asia/Novosibirsk", VisitReminderHoursBefore = 24 };
        var client = new OrganizationClient { Organization = organization, Name = "Алексей", TelegramNotificationsEnabled = true, TelegramChatId = 123,
            TelegramConnectedAt = VisitReminderTests.Now.AddDays(-1) };
        organization.Members.Add(new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner });
        var vehicle = new Vehicle { User = user, Organization = organization, OrganizationClient = client, RegistrationPlate = "А123АА154" };
        var work = new MaintenanceEntity { User = user, Vehicle = vehicle, Status = MaintenanceStatus.Planned, Type = MaintenanceType.Maintenance,
            PlannedDate = VisitReminderTests.Now.Date.AddDays(1), PlannedStartTime = TimeSpan.FromHours(10), Date = VisitReminderTests.Now, DateTimeCreation = VisitReminderTests.Now };

        return work;
    }

    private static async Task SaveWorkAsync(OpenCRMContext context, MaintenanceEntity work)
    {
        context.Add(work);
        context.OrganizationTelegramBots.Add(new OrganizationTelegramBot
        {
            Organization = work.Vehicle.Organization!, Username = $"qa_{work.Id:N}",
            ProtectedToken = "qa", ProtectedWebhookSecret = "qa", IsEnabled = true
        });
        await context.SaveChangesAsync();
    }
}
