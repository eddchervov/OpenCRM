using OpenCRM.Testing;
using Microsoft.Extensions.Configuration;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Modules.Notifications.Emails;
using OpenCRM.Modules.Notifications;
using OpenCRM.Services.Interfaces.Admin;
using Notifications.Contracts;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

[Collection(nameof(NotificationDatabaseCollection))]
public sealed class NotificationIntegrationTests(NotificationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CustomHtmlIsSentAsDataAndRenderedContentReturnsToJournal()
    {
        await using var context = fixture.CreateContext();
        await fixture.CreateSender(context).QueueAsync("test@example.com", "Тема", "<p>{{текст}} @(1+1)</p>", "html-test");
        Assert.Null((await context.EmailMessages.SingleAsync()).SentAt);
        await fixture.CreateDispatcher(context).DispatchNextAsync(CancellationToken.None);
        var command = await fixture.NextCommandAsync<SendNotification>();
        Assert.Equal("system/custom-email", command.Request.TemplateKey);
        Assert.Equal("<p>{{текст}} @(1+1)</p>", command.Request.Data!["htmlBody"].GetString());
        Assert.Null(command.Request.Body);

        await fixture.ReplyAsync(this.Status("html-test", "provider-accepted", providerId: "provider123") with
        {
            Content = new NotificationContent("Готовая тема", "<html>Полное письмо</html>", true, "Полное письмо")
        });
        await fixture.WaitAsync(db => db.EmailMessages.AnyAsync(x => x.SentAt != null && x.NextDispatchAt == null));
        context.ChangeTracker.Clear();
        var message = await context.EmailMessages.SingleAsync();
        Assert.Equal("provider123", message.ProviderMessageId);
        Assert.Equal("<html>Полное письмо</html>", message.HtmlBody);
        Assert.Equal("Полное письмо", message.TextBody);
        Assert.Equal("Готовая тема", message.Subject);
    }

    [Fact]
    public async Task RazorCommandCarriesDataWithoutRegisteringHtml()
    {
        await using var context = fixture.CreateContext();
        await fixture.CreateSender(context).QueueTemplateAsync(
            "test@example.com", "Подтверждение", "system/email-confirmation",
            new { name = "Иван", confirmationUrl = "https://example.com/confirm?code=test" });
        await fixture.CreateDispatcher(context).DispatchNextAsync(CancellationToken.None);
        var command = await fixture.NextCommandAsync<SendNotification>();
        Assert.Equal("system/email-confirmation", command.Request.TemplateKey);
        Assert.Equal("Иван", command.Request.Data!["name"].GetString());
        Assert.Null(command.Request.Subject);
        Assert.Null(command.Request.Body);
    }

    [Fact]
    public async Task BrokerFailureKeepsIntentForRetry()
    {
        await using var context = fixture.CreateContext();
        await fixture.CreateSender(context).QueueAsync("test@example.com", "Тема", "<p>Текст</p>", "offline");
        var endpoints = new Mock<ISendEndpointProvider>();
        endpoints.Setup(x => x.GetSendEndpoint(It.IsAny<Uri>())).ThrowsAsync(new IOException("Недоступен брокер."));
        var dispatcher = new NotificationOutboxDispatcher(context, endpoints.Object, Options.Create(fixture.Options));
        await Assert.ThrowsAsync<IOException>(() => dispatcher.DispatchNextAsync(CancellationToken.None));
        context.ChangeTracker.Clear();
        var message = await context.EmailMessages.SingleAsync();
        Assert.NotNull(message.NextDispatchAt);
        Assert.Null(message.NotificationCommandId);
        Assert.Null(message.SentAt);
    }

    [Fact]
    public async Task QueueUsesStableIdempotencyKey()
    {
        await using var context = fixture.CreateContext();
        var sender = fixture.CreateSender(context);
        await sender.QueueAsync("test@example.com", "Тема", "<p>Текст</p>", "duplicate");
        await sender.QueueAsync("test@example.com", "Тема", "<p>Текст</p>", "duplicate");
        Assert.Equal(1, await context.EmailMessages.CountAsync());
    }

    [Theory]
    [InlineData("simulated", 0, 1, 0)]
    [InlineData("provider-accepted", 1, 0, 0)]
    [InlineData("failed", 0, 0, 1)]
    public async Task CampaignCountersReflectWorkerResultExactlyOnce(string status, int sent, int simulated, int failed)
    {
        await using var context = fixture.CreateContext();
        var recipient = new EmailCampaignRecipient { Email = "test@example.com", StatusCode = EmailCampaignRecipientStatusCodes.Queued };
        var campaign = new EmailCampaign
        {
            Subject = "Тема",
            HtmlBody = "<p>Текст</p>",
            StatusCode = EmailCampaignStatusCodes.Running,
            TotalCount = 1,
            Recipients = [recipient]
        };
        context.EmailCampaigns.Add(campaign);
        await context.SaveChangesAsync();
        await fixture.CreateSender(context).QueueAsync(recipient.Email, campaign.Subject, campaign.HtmlBody, "campaign", recipient.Id);
        var result = this.Status("campaign", status);
        await fixture.ReplyAsync(result);
        await fixture.WaitAsync(db => db.EmailCampaigns.AnyAsync(x => x.StatusCode == EmailCampaignStatusCodes.Completed));
        // Прямой повтор проверяет обработчик синхронно, без ожидания транспортной очереди.
        var consumer = new NotificationResultConsumer(context, Options.Create(fixture.Options));
        var repeated = new Mock<ConsumeContext<NotificationStatusChanged>>();
        repeated.SetupGet(x => x.Message).Returns(result);
        await consumer.Consume(repeated.Object);
        repeated.SetupGet(x => x.Message).Returns(this.Status("campaign", "queued") with { AttemptCount = 0 });
        await consumer.Consume(repeated.Object);

        context.ChangeTracker.Clear();
        campaign = await context.EmailCampaigns.SingleAsync();
        Assert.Equal(sent, campaign.SentCount);
        Assert.Equal(simulated, campaign.SimulatedCount);
        Assert.Equal(failed, campaign.FailedCount);
        var message = await context.EmailMessages.SingleAsync();
        Assert.Equal(status, message.NotificationStatus);
        Assert.Equal(sent == 1, message.SentAt != null);
    }

    [Fact]
    public async Task CampaignQueueDoesNotCountAsSent()
    {
        await using var context = fixture.CreateContext();
        var recipient = new EmailCampaignRecipient { Email = "test@example.com" };
        var campaign = new EmailCampaign
        {
            Subject = "Тема", HtmlBody = "<p>Текст</p>", TotalCount = 1,
            StartAt = DateTime.UtcNow.AddMinutes(-1), Recipients = [recipient]
        };
        context.EmailCampaigns.Add(campaign);
        await context.SaveChangesAsync();
        var service = new EmailCampaignService(context, fixture.CreateSender(context),
            Mock.Of<IEmailCampaignScheduler>(), NullLogger<EmailCampaignService>.Instance);
        await service.ProcessCampaignAsync(campaign.Id);
        context.ChangeTracker.Clear();
        campaign = await context.EmailCampaigns.SingleAsync();
        recipient = await context.EmailCampaignRecipients.SingleAsync();
        Assert.Equal(0, campaign.SentCount);
        Assert.Equal(EmailCampaignStatusCodes.Running, campaign.StatusCode);
        Assert.Equal(EmailCampaignRecipientStatusCodes.Queued, recipient.StatusCode);
        Assert.Equal(recipient.Id, (await context.EmailMessages.SingleAsync()).CampaignRecipientId);
    }

    [Fact]
    public async Task TerminalResultPreservesProviderDeliveryStatus()
    {
        await using var context = fixture.CreateContext();
        await fixture.CreateSender(context).QueueAsync("test@example.com", "Тема", "<p>Текст</p>", "opened");
        var message = await context.EmailMessages.SingleAsync();
        message.StatusCode = EmailMessageStatusCodes.Opened;
        message.OpenCount = 1;
        await context.SaveChangesAsync();
        await fixture.ReplyAsync(this.Status("opened", "provider-accepted"));
        await fixture.WaitAsync(db => db.EmailMessages.AnyAsync(x => x.NextDispatchAt == null));
        context.ChangeTracker.Clear();
        message = await context.EmailMessages.SingleAsync();
        Assert.Equal(EmailMessageStatusCodes.Opened, message.StatusCode);
        Assert.Equal(1, message.OpenCount);
    }

    [Fact]
    public async Task RejectedCommandFinishesLocalIntent()
    {
        await using var context = fixture.CreateContext();
        await fixture.CreateSender(context).QueueAsync("test@example.com", "Тема", "<p>Текст</p>", "rejected");
        await fixture.CreateDispatcher(context).DispatchNextAsync(CancellationToken.None);
        await fixture.NextCommandAsync<SendNotification>();
        context.ChangeTracker.Clear();
        var message = await context.EmailMessages.SingleAsync();
        await fixture.ReplyAsync(new NotificationCommandRejected(Guid.NewGuid(), fixture.Options.ApplicationId,
            message.NotificationCommandId, nameof(SendNotification), "invalid-command"));
        await fixture.WaitAsync(db => db.EmailMessages.AnyAsync(x => x.StatusCode == EmailMessageStatusCodes.Failed));
        context.ChangeTracker.Clear();
        Assert.Null((await context.EmailMessages.SingleAsync()).NextDispatchAt);
    }

    [Fact]
    public async Task MissingRemoteRecordReplaysOriginalKey()
    {
        await using var context = fixture.CreateContext();
        await fixture.CreateSender(context).QueueTemplateAsync(
            "test@example.com", "Тема", "system/password-reset", new { name = "Иван", resetUrl = "https://example.com" });
        await fixture.CreateDispatcher(context).DispatchNextAsync(CancellationToken.None);
        var first = await fixture.NextCommandAsync<SendNotification>();
        await fixture.ReplyAsync(this.Status(first.Request.IdempotencyKey, "not-found") with { AttemptCount = 0 });
        await fixture.WaitAsync(db => db.EmailMessages.AnyAsync(x => x.NotificationDispatchedAt == null));
        await fixture.CreateDispatcher(context).DispatchNextAsync(CancellationToken.None);
        var second = await fixture.NextCommandAsync<SendNotification>();
        Assert.Equal(first.Request.IdempotencyKey, second.Request.IdempotencyKey);
    }

    [Fact]
    public async Task ModelMatchesGeneratedMigration()
    {
        await using var context = fixture.CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Contains("20260909083524_AddNotificationOutbox", await context.Database.GetAppliedMigrationsAsync());
    }

    [Theory]
    [InlineData("simulated", false)]
    [InlineData("provider-accepted", true)]
    [InlineData("failed", false)]
    public async Task ReminderSentAtReflectsRealProviderAcceptance(string status, bool isSent)
    {
        await using var context = fixture.CreateContext();
        var reminder = new SubscriptionExpiryReminder
        {
            ScopeCode = "personal", SubscriptionId = 1, UserId = "test-user",
            SubscriptionEndDate = DateTime.UtcNow.AddDays(1), EventCode = "test",
            RecipientEmail = "test@example.com", NotificationQueuedAt = DateTime.UtcNow
        };
        context.SubscriptionExpiryReminders.Add(reminder);
        await context.SaveChangesAsync();
        await fixture.CreateSender(context).QueueTemplateAsync(
            reminder.RecipientEmail, "Тема", "system/subscription-notice", new { kind = "upcoming" },
            "reminder-test", reminder.Id);
        await fixture.ReplyAsync(this.Status("reminder-test", status));
        await fixture.WaitAsync(db => db.EmailMessages.AnyAsync(x => x.NextDispatchAt == null));
        context.ChangeTracker.Clear();
        reminder = await context.SubscriptionExpiryReminders.SingleAsync(x => x.Id == reminder.Id);
        Assert.Equal(isSent, reminder.SentAt != null);
        Assert.NotNull(reminder.NotificationQueuedAt);
    }

    [Fact]
    public async Task MigrationRollbackRejectsUnprocessedIntent()
    {
        await using var context = fixture.CreateContext();
        await fixture.CreateSender(context).QueueAsync("test@example.com", "Тема", "<p>Текст</p>", "rollback");
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
            .GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(context);
        try
        {
            var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                migrator.MigrateAsync("20260905053253_FixSubscriptionPaymentDatesAndAudit"));
            Assert.Equal("P0001", exception.SqlState);
            Assert.Equal(1, await context.EmailMessages.CountAsync());
        }
        finally
        {
            await migrator.MigrateAsync();
        }
    }

    [Fact]
    public async Task MigrationRoundTripPreservesLegacyEmail()
    {
        await using var context = fixture.CreateContext();
        context.EmailMessages.Add(new EmailMessage
        {
            ToEmail = "legacy@example.com", Subject = "Старое письмо",
            HtmlBody = "<p>Содержимое</p>", StatusCode = EmailMessageStatusCodes.Sent, SentAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
            .GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(context);
        try
        {
            await migrator.MigrateAsync("20260905053253_FixSubscriptionPaymentDatesAndAudit");
        }
        finally
        {
            await migrator.MigrateAsync();
        }

        context.ChangeTracker.Clear();
        var message = await context.EmailMessages.SingleAsync();
        Assert.Equal("Старое письмо", message.Subject);
        Assert.Equal("<p>Содержимое</p>", message.HtmlBody);
        Assert.NotNull(message.SentAt);
        Assert.Null(message.NotificationKey);
        Assert.Null(message.NextDispatchAt);
    }

    [Fact]
    public async Task ForeignAndOlderResultsCannotReplaceCurrentStatus()
    {
        await using var context = fixture.CreateContext();
        await fixture.CreateSender(context).QueueAsync("test@example.com", "Тема", "<p>Текст</p>", "ordering");
        var consumer = new NotificationResultConsumer(context, Options.Create(fixture.Options));
        var incoming = new Mock<ConsumeContext<NotificationStatusChanged>>();
        incoming.SetupGet(x => x.Message).Returns(this.Status("ordering", "failed") with { ApplicationId = "foreign" });
        await consumer.Consume(incoming.Object);
        var result = this.Status("ordering", "retrying") with { AttemptCount = 2 };
        incoming.SetupGet(x => x.Message).Returns(result);
        await consumer.Consume(incoming.Object);
        incoming.SetupGet(x => x.Message).Returns(this.Status("ordering", "queued") with
        {
            AttemptCount = 1, OccurredAt = result.OccurredAt.AddMinutes(-1)
        });
        await consumer.Consume(incoming.Object);
        context.ChangeTracker.Clear();
        Assert.Equal("retrying", (await context.EmailMessages.SingleAsync()).NotificationStatus);
    }

    [Fact]
    public async Task DeliveryEventsAreDeduplicatedAndOlderEventsDoNotReplaceStatus()
    {
        await using var context = fixture.CreateContext();
        await fixture.CreateSender(context).QueueAsync("test@example.com", "Тема", "<p>Текст</p>", "delivery");
        var consumer = new NotificationDeliveryConsumer(context, Options.Create(fixture.Options));
        var incoming = new Mock<ConsumeContext<NotificationDeliveryReported>>();
        var clicked = new NotificationDeliveryReported(Guid.NewGuid(), fixture.Options.ApplicationId,
            "delivery", "clicked", DateTimeOffset.UtcNow, "test@example.com", "provider", Url: "https://example.com");
        incoming.SetupGet(x => x.Message).Returns(clicked);
        await consumer.Consume(incoming.Object);
        await consumer.Consume(incoming.Object);
        incoming.SetupGet(x => x.Message).Returns(clicked with
        {
            EventId = Guid.NewGuid(), Status = "opened", OccurredAt = clicked.OccurredAt.AddSeconds(-10)
        });
        await consumer.Consume(incoming.Object);
        incoming.SetupGet(x => x.Message).Returns(clicked with
        {
            EventId = Guid.NewGuid(), ApplicationId = "foreign", Status = "failed"
        });
        await consumer.Consume(incoming.Object);

        context.ChangeTracker.Clear();
        var message = await context.EmailMessages.SingleAsync();
        Assert.Equal("clicked", message.StatusCode);
        Assert.Equal(1, message.ClickCount);
        Assert.Equal(1, message.OpenCount);
        Assert.Equal(2, await context.EmailMessageEvents.CountAsync());
    }

    [Fact]
    public async Task LateContentRestoresJournalWithoutChangingTerminalStatus()
    {
        await using var context = fixture.CreateContext();
        await fixture.CreateSender(context).QueueAsync("test@example.com", "Тема", "<p>Текст</p>", "late-content");
        var consumer = new NotificationResultConsumer(context, Options.Create(fixture.Options));
        var incoming = new Mock<ConsumeContext<NotificationStatusChanged>>();
        incoming.SetupGet(x => x.Message).Returns(this.Status("late-content", "provider-accepted"));
        await consumer.Consume(incoming.Object);
        incoming.SetupGet(x => x.Message).Returns(this.Status("late-content", "queued") with
        {
            Content = new NotificationContent("Тема", "<html>Текст</html>", true, "Текст"), AttemptCount = 0
        });
        await consumer.Consume(incoming.Object);
        context.ChangeTracker.Clear();
        var message = await context.EmailMessages.SingleAsync();
        Assert.Equal("provider-accepted", message.NotificationStatus);
        Assert.Equal("<html>Текст</html>", message.HtmlBody);
        Assert.Null(message.NextDispatchAt);
    }

    private NotificationStatusChanged Status(string key, string status, string? providerId = null)
    {
        return new NotificationStatusChanged(Guid.NewGuid(), fixture.Options.ApplicationId,
            key, Guid.NewGuid(), status, 1, DateTimeOffset.UtcNow, providerId);
    }
}
