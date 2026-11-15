using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Notifications;
using OpenCRM.Testing;
using Notifications.Contracts;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

[Collection(nameof(NotificationDatabaseCollection))]
public sealed class NotificationConsumerEdgeTests(NotificationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("spam")]
    [InlineData("unsubscribed")]
    public async Task DeliveryRemovesAdvertisingConsentAndPreservesProviderId(string status)
    {
        await using var context = fixture.CreateContext();
        var email = $"{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            Email = email, NormalizedEmail = email.ToUpperInvariant(),
            AdvertisingConsentAccepted = true, AdvertisingConsentAcceptedAt = DateTime.UtcNow
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var key = Guid.NewGuid().ToString();
        await fixture.CreateSender(context).QueueAsync(email, "Тема", "Текст", key);
        var message = await context.EmailMessages.SingleAsync(item => item.NotificationKey == key);
        message.ProviderMessageId = "original-provider";
        await context.SaveChangesAsync();
        var report = new NotificationDeliveryReported(Guid.NewGuid(), fixture.Options.ApplicationId, key,
            status, DateTimeOffset.UtcNow, email.ToUpperInvariant(), "new-provider",
            Url: new string('u', 2001), IpAddress: new string('i', 101), UserAgent: new string('a', 1001));
        var consumer = new NotificationDeliveryConsumer(context, Options.Create(fixture.Options));

        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report));
        context.ChangeTracker.Clear();
        user = await context.Users.SingleAsync(item => item.Id == user.Id);
        message = await context.EmailMessages.SingleAsync(item => item.NotificationKey == key);
        var savedEvent = await context.EmailMessageEvents.SingleAsync(item => item.NotificationEventId == report.EventId);
        Assert.False(user.AdvertisingConsentAccepted);
        Assert.Null(user.AdvertisingConsentAcceptedAt);
        Assert.Equal(status, message.StatusCode);
        Assert.Equal("original-provider", message.ProviderMessageId);
        Assert.Equal(2000, savedEvent.Url!.Length);
        Assert.Equal(100, savedEvent.IpAddress!.Length);
        Assert.Equal(1000, savedEvent.UserAgent!.Length);
    }

    [Fact]
    public async Task UnknownDeliveryAndBlankKeysDoNotCreateJournalEntries()
    {
        await using var context = fixture.CreateContext();
        var consumer = new NotificationDeliveryConsumer(context, Options.Create(fixture.Options));
        var report = new NotificationDeliveryReported(Guid.NewGuid(), fixture.Options.ApplicationId,
            Guid.NewGuid().ToString(), "delivered", DateTimeOffset.UtcNow, null, null);

        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report));
        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report with { IdempotencyKey = " " }));
        Assert.False(await context.EmailMessageEvents.AnyAsync(item => item.NotificationEventId == report.EventId));
    }

    [Theory]
    [InlineData("queued", "queued")]
    [InlineData("processing", "queued")]
    [InlineData("retrying", "retrying")]
    [InlineData("provider-accepted", "sent")]
    [InlineData("simulated", "simulated")]
    [InlineData("failed", "failed")]
    public async Task EmailStatusUpdatesPublicDisplayAndTerminalScheduling(string status, string displayStatus)
    {
        await using var context = fixture.CreateContext();
        var key = Guid.NewGuid().ToString();
        await fixture.CreateSender(context).QueueAsync("test@example.com", "Тема", "Текст", key);
        var consumer = new NotificationResultConsumer(context, Options.Create(fixture.Options));
        var report = new NotificationStatusChanged(Guid.NewGuid(), fixture.Options.ApplicationId, key,
            Guid.NewGuid(), status, 2, DateTimeOffset.UtcNow, "provider", "error");

        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report));
        context.ChangeTracker.Clear();
        var message = await context.EmailMessages.SingleAsync(item => item.NotificationKey == key);
        Assert.Equal(status, message.NotificationStatus);
        Assert.Equal(displayStatus, message.StatusCode);
        Assert.Equal(2, message.NotificationAttemptCount);
        Assert.Equal("provider", message.ProviderMessageId);
        if (status is "provider-accepted" or "simulated" or "failed")
        {
            Assert.Null(message.NextDispatchAt);
        }
        else
        {
            Assert.NotNull(message.NextDispatchAt);
        }
    }

    [Fact]
    public async Task UnknownStatusRollsBackAndForeignReportsAreIgnored()
    {
        await using var context = fixture.CreateContext();
        var key = Guid.NewGuid().ToString();
        await fixture.CreateSender(context).QueueAsync("test@example.com", "Тема", "Текст", key);
        var consumer = new NotificationResultConsumer(context, Options.Create(fixture.Options));
        var report = new NotificationStatusChanged(Guid.NewGuid(), fixture.Options.ApplicationId, key,
            Guid.NewGuid(), "invalid", 1, DateTimeOffset.UtcNow);

        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report with { ApplicationId = "foreign" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report)));
        await using var verification = fixture.CreateContext();
        Assert.Equal("pending", (await verification.EmailMessages.SingleAsync(item => item.NotificationKey == key)).NotificationStatus);
    }

    [Theory]
    [InlineData("provider-accepted", "sent", 1, 0, 0)]
    [InlineData("simulated", "simulated", 0, 1, 0)]
    [InlineData("failed", "failed", 0, 0, 1)]
    public async Task TerminalCampaignResultCompletesRecipientAndCountsExactlyOnce(
        string status, string recipientStatus, int sent, int simulated, int failed)
    {
        await using var context = fixture.CreateContext();
        var campaign = new EmailCampaign { TotalCount = 1 };
        var recipient = new EmailCampaignRecipient { EmailCampaign = campaign, Email = "test@example.com", StatusCode = "queued" };
        context.EmailCampaignRecipients.Add(recipient);
        await context.SaveChangesAsync();
        var key = Guid.NewGuid().ToString();
        await fixture.CreateSender(context).QueueAsync("test@example.com", "Тема", "Текст", key, recipient.Id);
        var consumer = new NotificationResultConsumer(context, Options.Create(fixture.Options));
        var report = new NotificationStatusChanged(Guid.NewGuid(), fixture.Options.ApplicationId, key,
            Guid.NewGuid(), status, 1, DateTimeOffset.UtcNow, "provider", "error");

        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report));
        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report with { OccurredAt = report.OccurredAt.AddSeconds(1) }));
        context.ChangeTracker.Clear();
        campaign = await context.EmailCampaigns.SingleAsync(item => item.Id == campaign.Id);
        recipient = await context.EmailCampaignRecipients.SingleAsync(item => item.Id == recipient.Id);
        Assert.Equal(recipientStatus, recipient.StatusCode);
        Assert.Equal(sent, campaign.SentCount);
        Assert.Equal(simulated, campaign.SimulatedCount);
        Assert.Equal(failed, campaign.FailedCount);
        Assert.Equal(EmailCampaignStatusCodes.Completed, campaign.StatusCode);
        Assert.NotNull(campaign.CompletedAt);
    }

    private static ConsumeContext<T> CreateConsume<T>(T message) where T : class
    {
        var consume = new Mock<ConsumeContext<T>>();
        consume.SetupGet(item => item.Message).Returns(message);

        return consume.Object;
    }

    [Fact]
    public async Task HistoricalEmailWithMissingRelatedRecordsStillFinishesDelivery()
    {
        await using var context = fixture.CreateContext();
        var key = Guid.NewGuid().ToString();
        await fixture.CreateSender(context).QueueAsync("test@example.com", "Тема", "Текст", key,
            campaignRecipientId: Guid.NewGuid(), subscriptionReminderId: long.MaxValue);
        var consumer = new NotificationResultConsumer(context, Options.Create(fixture.Options));
        var report = new NotificationStatusChanged(Guid.NewGuid(), fixture.Options.ApplicationId, key,
            Guid.NewGuid(), "provider-accepted", 1, DateTimeOffset.UtcNow);

        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report));
        context.ChangeTracker.Clear();
        var message = await context.EmailMessages.SingleAsync(item => item.NotificationKey == key);
        Assert.Equal("provider-accepted", message.NotificationStatus);
        Assert.NotNull(message.SentAt);
        Assert.Null(message.NextDispatchAt);
    }

    [Theory]
    [InlineData("sent")]
    [InlineData("failed")]
    [InlineData("simulated")]
    public async Task AlreadyFinishedCampaignRecipientDoesNotIncrementCounters(string recipientStatus)
    {
        await using var context = fixture.CreateContext();
        var campaign = new EmailCampaign { TotalCount = 1 };
        var recipient = new EmailCampaignRecipient
        {
            EmailCampaign = campaign, Email = "test@example.com", StatusCode = recipientStatus
        };
        context.EmailCampaignRecipients.Add(recipient);
        await context.SaveChangesAsync();
        var key = Guid.NewGuid().ToString();
        await fixture.CreateSender(context).QueueAsync("test@example.com", "Тема", "Текст", key, recipient.Id);
        var consumer = new NotificationResultConsumer(context, Options.Create(fixture.Options));
        var report = new NotificationStatusChanged(Guid.NewGuid(), fixture.Options.ApplicationId, key,
            Guid.NewGuid(), "provider-accepted", 1, DateTimeOffset.UtcNow);

        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report));
        context.ChangeTracker.Clear();
        campaign = await context.EmailCampaigns.SingleAsync(item => item.Id == campaign.Id);
        Assert.Equal(0, campaign.SentCount + campaign.FailedCount + campaign.SimulatedCount);
        Assert.Equal(recipientStatus, (await context.EmailCampaignRecipients.SingleAsync(item => item.Id == recipient.Id)).StatusCode);
    }

    [Theory]
    [InlineData("scheduled", 2)]
    [InlineData("canceled", 1)]
    public async Task TerminalResultDoesNotCompleteUnfinishedOrCanceledCampaign(string status, int total)
    {
        await using var context = fixture.CreateContext();
        var campaign = new EmailCampaign { TotalCount = total, StatusCode = status };
        var recipient = new EmailCampaignRecipient { EmailCampaign = campaign, Email = "test@example.com", StatusCode = "queued" };
        context.EmailCampaignRecipients.Add(recipient);
        await context.SaveChangesAsync();
        var key = Guid.NewGuid().ToString();
        await fixture.CreateSender(context).QueueAsync("test@example.com", "Тема", "Текст", key, recipient.Id);
        var consumer = new NotificationResultConsumer(context, Options.Create(fixture.Options));
        var report = new NotificationStatusChanged(Guid.NewGuid(), fixture.Options.ApplicationId, key,
            Guid.NewGuid(), "provider-accepted", 1, DateTimeOffset.UtcNow);

        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report));
        context.ChangeTracker.Clear();
        campaign = await context.EmailCampaigns.SingleAsync(item => item.Id == campaign.Id);
        Assert.Equal(status, campaign.StatusCode);
        Assert.Equal(1, campaign.SentCount);
        Assert.Null(campaign.CompletedAt);
    }

    [Fact]
    public async Task UnsubscribeWithoutRecipientKeepsExistingConsent()
    {
        await using var context = fixture.CreateContext();
        var email = $"{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser { Email = email, NormalizedEmail = email.ToUpperInvariant(), AdvertisingConsentAccepted = true };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var key = Guid.NewGuid().ToString();
        await fixture.CreateSender(context).QueueAsync(email, "Тема", "Текст", key);
        var consumer = new NotificationDeliveryConsumer(context, Options.Create(fixture.Options));
        var report = new NotificationDeliveryReported(Guid.NewGuid(), fixture.Options.ApplicationId, key,
            "unsubscribed", DateTimeOffset.UtcNow, null, null);

        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report));
        await using var verification = fixture.CreateContext();
        Assert.True((await verification.Users.SingleAsync(item => item.Id == user.Id)).AdvertisingConsentAccepted);
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("processing")]
    [InlineData("retrying")]
    [InlineData("provider-accepted")]
    [InlineData("simulated")]
    [InlineData("failed")]
    public async Task TelegramAdminResultUpdatesTrackedIntent(string status)
    {
        await using var context = fixture.CreateContext();
        var key = Guid.NewGuid().ToString();
        context.TelegramNotificationOutbox.Add(new TelegramNotificationOutbox { NotificationKey = key, Body = "Текст" });
        await context.SaveChangesAsync();
        var consumer = new NotificationResultConsumer(context, Options.Create(fixture.Options));
        var report = new NotificationStatusChanged(Guid.NewGuid(), fixture.Options.ApplicationId, key,
            Guid.NewGuid(), status, 2, DateTimeOffset.UtcNow, "provider", "error");

        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report));
        context.ChangeTracker.Clear();
        var message = await context.TelegramNotificationOutbox.SingleAsync(item => item.NotificationKey == key);
        Assert.Equal(status, message.NotificationStatus);
        Assert.Equal(2, message.AttemptCount);
        Assert.Equal(report.NotificationId, message.NotificationId);
        Assert.Equal("provider", message.ProviderMessageId);
        Assert.Equal("error", message.ErrorCode);
    }

    [Fact]
    public async Task TelegramOutboxIgnoresOlderSnapshotsAndRestartsAfterNotFound()
    {
        await using var context = fixture.CreateContext();
        var key = Guid.NewGuid().ToString();
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-1);
        context.TelegramNotificationOutbox.Add(new TelegramNotificationOutbox
        {
            NotificationKey = key, Body = "Текст", AttemptCount = 2, NotificationStatus = "processing",
            NotificationEventAt = timestamp.UtcDateTime, NotificationDispatchedAt = timestamp.UtcDateTime,
            ProviderMessageId = "original"
        });
        await context.SaveChangesAsync();
        var consumer = new NotificationResultConsumer(context, Options.Create(fixture.Options));
        var report = new NotificationStatusChanged(Guid.NewGuid(), fixture.Options.ApplicationId, key,
            Guid.NewGuid(), "queued", 2, timestamp.AddSeconds(-1));

        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report));
        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report with { OccurredAt = timestamp.AddSeconds(1), AttemptCount = 1 }));
        context.ChangeTracker.Clear();
        Assert.Equal("processing", (await context.TelegramNotificationOutbox.SingleAsync(item => item.NotificationKey == key)).NotificationStatus);
        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report with { OccurredAt = timestamp.AddSeconds(2), Status = "not-found" }));
        context.ChangeTracker.Clear();
        var message = await context.TelegramNotificationOutbox.SingleAsync(item => item.NotificationKey == key);
        Assert.Null(message.NotificationDispatchedAt);
        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report with { OccurredAt = timestamp.AddSeconds(3), ProviderMessageId = null }));
        context.ChangeTracker.Clear();
        Assert.Equal("original", (await context.TelegramNotificationOutbox.SingleAsync(item => item.NotificationKey == key)).ProviderMessageId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(
            report with { OccurredAt = timestamp.AddSeconds(4), Status = "unknown" })));
    }

    [Fact]
    public async Task RejectionMatchesTelegramCommandAndForeignOrUncorrelatedRejectionsAreIgnored()
    {
        await using var context = fixture.CreateContext();
        var key = Guid.NewGuid().ToString();
        var commandId = Guid.NewGuid();
        context.TelegramNotificationOutbox.Add(new TelegramNotificationOutbox
        {
            NotificationKey = key, Body = "Текст", NotificationCommandId = commandId
        });
        await context.SaveChangesAsync();
        var consumer = new NotificationResultConsumer(context, Options.Create(fixture.Options));
        var report = new NotificationCommandRejected(Guid.NewGuid(), fixture.Options.ApplicationId, commandId, "SendNotification", "invalid");

        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report with { ApplicationId = "foreign" }));
        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report with { MessageId = null }));
        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report));
        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report));
        context.ChangeTracker.Clear();
        var message = await context.TelegramNotificationOutbox.SingleAsync(item => item.NotificationKey == key);
        Assert.Equal("failed", message.NotificationStatus);
        Assert.Equal("invalid", message.ErrorCode);
    }

    [Fact]
    public async Task PlainContentWithoutSubjectPreservesExistingSubject()
    {
        await using var context = fixture.CreateContext();
        var key = Guid.NewGuid().ToString();
        await fixture.CreateSender(context).QueueAsync("test@example.com", "Исходная тема", "Текст", key);
        var consumer = new NotificationResultConsumer(context, Options.Create(fixture.Options));
        var report = new NotificationStatusChanged(Guid.NewGuid(), fixture.Options.ApplicationId, key,
            Guid.NewGuid(), "queued", 1, DateTimeOffset.UtcNow,
            Content: new NotificationContent(null, "Текст", false, "Текст"));

        await consumer.Consume(NotificationConsumerEdgeTests.CreateConsume(report));
        context.ChangeTracker.Clear();
        var message = await context.EmailMessages.SingleAsync(item => item.NotificationKey == key);
        Assert.Equal("Исходная тема", message.Subject);
        Assert.Equal(string.Empty, message.HtmlBody);
        Assert.Equal("Текст", message.TextBody);
    }
}
