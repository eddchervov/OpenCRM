using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Modules.Notifications;
using OpenCRM.Infrastructure.Data;
using Notifications.Contracts;
using Npgsql;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

public sealed class ClientNotificationResultTests(ClientNotificationDatabaseFixture fixture)
    : IClassFixture<ClientNotificationDatabaseFixture>, IAsyncLifetime
{
    private const string ApplicationId = "client-notification-result-test";
    private static readonly DateTimeOffset ResultTime = new(2026, 10, 12, 8, 30, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await using (var context = fixture.CreateContext())
        {
            await context.TelegramClientNotifications.ExecuteDeleteAsync();
        }

        await fixture.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("max")]
    [InlineData("telegram")]
    public async Task SnapshotWithoutProviderIdPreservesPreviouslyReportedId(string channel)
    {
        var seeded = await this.CreateNotificationAsync(channel);
        await using (var context = fixture.CreateContext())
        {
            if (channel == "max")
            {
                (await context.MaxClientNotifications.SingleAsync(item => item.Id == seeded.Id)).ProviderMessageId = "original";
            }
            else
            {
                (await context.TelegramClientNotifications.SingleAsync(item => item.Id == seeded.Id)).ProviderMessageId = "original";
            }

            await context.SaveChangesAsync();
        }

        await this.ConsumeAsync(this.CreateResult(seeded.NotificationKey, "queued", Guid.NewGuid(), attemptCount: 1)
            with { ProviderMessageId = null });

        Assert.Equal("original", (await this.LoadNotificationAsync(channel, seeded.Id)).ProviderMessageId);
    }

    [Theory]
    [InlineData("max", "queued", "processing")]
    [InlineData("max", "processing", "processing")]
    [InlineData("max", "retrying", "retrying")]
    [InlineData("telegram", "queued", "processing")]
    [InlineData("telegram", "processing", "processing")]
    [InlineData("telegram", "retrying", "retrying")]
    public async Task StatusResultsPersistExpectedStateAndMetadata(string channel, string sourceStatus, string expectedStatus)
    {
        var seeded = await this.CreateNotificationAsync(channel);
        var notificationId = Guid.NewGuid();
        var result = this.CreateResult(seeded.NotificationKey, sourceStatus, notificationId, attemptCount: 2);

        await this.ConsumeAsync(result);

        var notification = await this.LoadNotificationAsync(channel, seeded.Id);
        Assert.Equal(expectedStatus, notification.Status);
        Assert.Equal(notificationId, notification.NotificationId);
        Assert.Equal("provider-message-42", notification.ProviderMessageId);
        Assert.Equal("provider-error", notification.ErrorCode);
        Assert.Equal(2, notification.AttemptCount);
        Assert.Equal(ResultTime.UtcDateTime, notification.NotificationEventAt);
        Assert.Null(notification.SentAt);
    }

    [Theory]
    [InlineData("max", "provider-accepted", "sent")]
    [InlineData("telegram", "provider-accepted", "sent")]
    [InlineData("max", "simulated", "simulated")]
    [InlineData("telegram", "simulated", "simulated")]
    [InlineData("max", "failed", "failed")]
    [InlineData("telegram", "failed", "failed")]
    public async Task TerminalResultsPersistExpectedSentAtAndMetadata(string channel, string sourceStatus, string expectedStatus)
    {
        var seeded = await this.CreateNotificationAsync(channel);
        var notificationId = Guid.NewGuid();
        var result = this.CreateResult(seeded.NotificationKey, sourceStatus, notificationId, attemptCount: 2);

        await this.ConsumeAsync(result);

        var notification = await this.LoadNotificationAsync(channel, seeded.Id);
        Assert.Equal(expectedStatus, notification.Status);
        Assert.Equal(notificationId, notification.NotificationId);
        Assert.Equal("provider-message-42", notification.ProviderMessageId);
        Assert.Equal("provider-error", notification.ErrorCode);
        Assert.Equal(2, notification.AttemptCount);
        Assert.Equal(ResultTime.UtcDateTime, notification.NotificationEventAt);
        Assert.Equal(sourceStatus == "provider-accepted" ? ResultTime.UtcDateTime : null, notification.SentAt);
    }

    [Theory]
    [InlineData("max", "sent")]
    [InlineData("max", "simulated")]
    [InlineData("max", "failed")]
    [InlineData("max", "skipped")]
    [InlineData("telegram", "sent")]
    [InlineData("telegram", "simulated")]
    [InlineData("telegram", "failed")]
    [InlineData("telegram", "skipped")]
    public async Task TerminalStoredStatusIsNotOverwritten(string channel, string terminalStatus)
    {
        var seeded = await this.CreateNotificationAsync(channel, terminalStatus);

        await this.ConsumeAsync(this.CreateResult(seeded.NotificationKey, "provider-accepted", Guid.NewGuid(), attemptCount: 9));

        var notification = await this.LoadNotificationAsync(channel, seeded.Id);
        Assert.Equal(terminalStatus, notification.Status);
        Assert.Equal(seeded.ExistingNotificationId, notification.NotificationId);
        Assert.Equal("existing-provider-id", notification.ProviderMessageId);
        Assert.Equal("existing-error", notification.ErrorCode);
        Assert.Equal(5, notification.AttemptCount);
        Assert.Equal(ResultTime.AddMinutes(-2).UtcDateTime, notification.NotificationEventAt);
        Assert.Equal(terminalStatus == "sent" ? ResultTime.AddMinutes(-2).UtcDateTime : null, notification.SentAt);
    }

    [Theory]
    [InlineData("max", "older")]
    [InlineData("max", "equal")]
    [InlineData("max", "lower-attempt")]
    [InlineData("max", "duplicate")]
    [InlineData("telegram", "older")]
    [InlineData("telegram", "equal")]
    [InlineData("telegram", "lower-attempt")]
    [InlineData("telegram", "duplicate")]
    public async Task DuplicateOlderOrLowerAttemptResultsDoNotChangeNotification(string channel, string staleKind)
    {
        if (staleKind == "duplicate")
        {
            var seededDuplicate = await this.CreateNotificationAsync(channel);
            var duplicateResult = this.CreateResult(
                seededDuplicate.NotificationKey,
                "retrying",
                Guid.NewGuid(),
                attemptCount: 2);

            await this.ConsumeAsync(duplicateResult);
            await this.ConsumeAsync(duplicateResult);

            var duplicateNotification = await this.LoadNotificationAsync(channel, seededDuplicate.Id);
            Assert.Equal("retrying", duplicateNotification.Status);
            Assert.Equal(duplicateResult.NotificationId, duplicateNotification.NotificationId);
            Assert.Equal(2, duplicateNotification.AttemptCount);
            Assert.Equal(ResultTime.UtcDateTime, duplicateNotification.NotificationEventAt);

            return;
        }

        var eventAt = ResultTime.AddMinutes(-1).UtcDateTime;
        const int attemptCount = 4;
        var seeded = await this.CreateNotificationAsync(channel, "processing", eventAt, attemptCount);
        var occurredAt = staleKind == "older" ? ResultTime.AddMinutes(-2) : staleKind == "equal" ? ResultTime.AddMinutes(-1) : ResultTime;
        var resultAttempt = staleKind == "lower-attempt" ? attemptCount - 1 : attemptCount;
        var result = this.CreateResult(seeded.NotificationKey, "failed", Guid.NewGuid(), resultAttempt, occurredAt);

        await this.ConsumeAsync(result);

        var notification = await this.LoadNotificationAsync(channel, seeded.Id);
        Assert.Equal("processing", notification.Status);
        Assert.Equal("existing-provider-id", notification.ProviderMessageId);
        Assert.Equal("existing-error", notification.ErrorCode);
        Assert.Equal(attemptCount, notification.AttemptCount);
        Assert.Equal(eventAt, notification.NotificationEventAt);
        Assert.Null(notification.NotificationId);
        Assert.Null(notification.SentAt);
    }

    [Theory]
    [InlineData("max")]
    [InlineData("telegram")]
    public async Task NotFoundRequeuesSameKeyWithoutAdvancingEventOrAttempt(string channel)
    {
        var dispatchAt = ResultTime.AddMinutes(-10).UtcDateTime;
        var eventAt = ResultTime.AddMinutes(-5).UtcDateTime;
        var seeded = await this.CreateNotificationAsync(channel, "processing", eventAt, attemptCount: 3, dispatchAt);
        var result = this.CreateResult(seeded.NotificationKey, "not-found", null, attemptCount: 3);

        await this.ConsumeAsync(result);

        var notification = await this.LoadNotificationAsync(channel, seeded.Id);
        Assert.Equal("queued", notification.Status);
        Assert.Equal(seeded.NotificationKey, notification.NotificationKey);
        Assert.Null(notification.NotificationDispatchedAt);
        Assert.True(notification.NextAttemptAt >= DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(eventAt, notification.NotificationEventAt);
        Assert.Equal(3, notification.AttemptCount);
        Assert.Null(notification.NotificationId);
    }

    [Theory]
    [InlineData("max")]
    [InlineData("telegram")]
    public async Task RejectedCommandFailsNotificationMatchedByCommandId(string channel)
    {
        var seeded = await this.CreateNotificationAsync(channel);
        var rejection = new NotificationCommandRejected(
            Guid.NewGuid(), ApplicationId, seeded.CommandId, "SendNotification", "invalid-command");
        var consume = new Mock<ConsumeContext<NotificationCommandRejected>>();
        consume.SetupGet(context => context.Message).Returns(rejection);
        await using var dbContext = fixture.CreateContext();
        var consumer = new NotificationResultConsumer(
            dbContext,
            Options.Create(new RabbitMqOptions { ApplicationId = ApplicationId, ResponseTimeoutSeconds = 30 }));
        await consumer.Consume(consume.Object);

        var notification = await this.LoadNotificationAsync(channel, seeded.Id);
        Assert.Equal("failed", notification.Status);
        Assert.Equal(seeded.CommandId, notification.NotificationCommandId);
        Assert.Equal("invalid-command", notification.ErrorCode);
        Assert.Null(notification.NotificationId);
        Assert.Equal(0, notification.AttemptCount);
        Assert.Null(notification.SentAt);
    }

    [Theory]
    [InlineData("max")]
    [InlineData("telegram")]
    public async Task ForeignApplicationResultIsIgnored(string channel)
    {
        var seeded = await this.CreateNotificationAsync(channel);
        var result = this.CreateResult(seeded.NotificationKey, "failed", Guid.NewGuid(), attemptCount: 7) with
        {
            ApplicationId = "another-application"
        };

        await this.ConsumeAsync(result);

        var notification = await this.LoadNotificationAsync(channel, seeded.Id);
        Assert.Equal("queued", notification.Status);
        Assert.Null(notification.NotificationId);
        Assert.Null(notification.ErrorCode);
        Assert.Equal(0, notification.AttemptCount);
    }

    [Theory]
    [InlineData("max")]
    [InlineData("telegram")]
    public async Task UnknownStatusThrowsAndRollsBack(string channel)
    {
        var seeded = await this.CreateNotificationAsync(channel);
        var result = this.CreateResult(seeded.NotificationKey, "unknown-status", Guid.NewGuid(), attemptCount: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => this.ConsumeAsync(result));

        var notification = await this.LoadNotificationAsync(channel, seeded.Id);
        Assert.Equal("queued", notification.Status);
        Assert.Null(notification.NotificationId);
        Assert.Null(notification.ErrorCode);
        Assert.Equal(0, notification.AttemptCount);
        Assert.Null(notification.NotificationEventAt);
    }

    [Theory]
    [InlineData("max")]
    [InlineData("telegram")]
    public async Task TransientConflictAfterSaveRetriesAndPersistsResult(string channel)
    {
        var seeded = await this.CreateNotificationAsync(channel);
        var interceptor = new TransientSaveFailureInterceptor();
        var result = this.CreateResult(seeded.NotificationKey, "retrying", Guid.NewGuid(), attemptCount: 2);

        await this.ConsumeAsync(result, interceptor);

        var notification = await this.LoadNotificationAsync(channel, seeded.Id);
        Assert.Equal(2, interceptor.SavedChangesAttempts);
        Assert.Equal("retrying", notification.Status);
        Assert.Equal(result.NotificationId, notification.NotificationId);
        Assert.Equal(2, notification.AttemptCount);
        Assert.Equal(ResultTime.UtcDateTime, notification.NotificationEventAt);
    }

    private async Task<(long Id, string NotificationKey, Guid CommandId, Guid? ExistingNotificationId)> CreateNotificationAsync(
        string channel,
        string status = "queued",
        DateTime? eventAt = null,
        int attemptCount = 0,
        DateTime? dispatchedAt = null)
    {
        if (channel is not ("max" or "telegram"))
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Неизвестный канал уведомлений.");
        }

        var graph = await fixture.CreateNotificationAsync();
        await using var context = fixture.CreateContext();
        var maxNotification = await context.MaxClientNotifications.SingleAsync(notification => notification.Id == graph.NotificationId);
        var commandId = Guid.NewGuid();
        maxNotification.Status = status;
        maxNotification.NotificationCommandId = commandId;
        maxNotification.NotificationDispatchedAt = dispatchedAt ?? ResultTime.AddMinutes(-1).UtcDateTime;
        var isTerminalSeed = status is "sent" or "simulated" or "failed" or "skipped";
        var existingNotificationId = isTerminalSeed ? Guid.NewGuid() : (Guid?)null;
        maxNotification.NotificationId = existingNotificationId;
        maxNotification.NotificationEventAt = eventAt ?? (isTerminalSeed ? ResultTime.AddMinutes(-2).UtcDateTime : null);
        maxNotification.AttemptCount = isTerminalSeed ? 5 : attemptCount;
        maxNotification.ProviderMessageId = status == "processing" || isTerminalSeed ? "existing-provider-id" : null;
        maxNotification.ErrorCode = status == "processing" || isTerminalSeed ? "existing-error" : null;
        maxNotification.SentAt = status == "sent" ? ResultTime.AddMinutes(-2).UtcDateTime : null;

        if (channel == "telegram")
        {
            context.TelegramClientNotifications.Add(new TelegramClientNotification
            {
                OrganizationId = maxNotification.OrganizationId,
                ClientId = maxNotification.ClientId,
                ChatId = maxNotification.MaxUserId,
                MaintenanceId = maxNotification.MaintenanceId,
                MaintenanceVersion = maxNotification.MaintenanceVersion,
                EventCode = maxNotification.EventCode,
                NotificationKey = maxNotification.NotificationKey,
                NotificationCommandId = commandId,
                NotificationId = maxNotification.NotificationId,
                NotificationDispatchedAt = maxNotification.NotificationDispatchedAt,
                NotificationEventAt = maxNotification.NotificationEventAt,
                Body = maxNotification.Body,
                Status = status,
                AttemptCount = maxNotification.AttemptCount,
                NextAttemptAt = maxNotification.NextAttemptAt,
                ProviderMessageId = maxNotification.ProviderMessageId,
                ErrorCode = maxNotification.ErrorCode,
                SentAt = maxNotification.SentAt
            });
            context.MaxClientNotifications.Remove(maxNotification);
        }

        await context.SaveChangesAsync();
        var notificationId = channel == "max"
            ? graph.NotificationId
            : await context.TelegramClientNotifications
                .Where(notification => notification.NotificationKey == graph.NotificationKey)
                .Select(notification => notification.Id)
                .SingleAsync();

        return (notificationId, graph.NotificationKey, commandId, existingNotificationId);
    }

    private async Task<ClientNotificationSnapshot> LoadNotificationAsync(string channel, long id)
    {
        await using var context = fixture.CreateContext();
        if (channel == "max")
        {
            var notification = await context.MaxClientNotifications.SingleAsync(item => item.Id == id);

            return new ClientNotificationSnapshot(
                notification.NotificationKey,
                notification.NotificationCommandId,
                notification.NotificationId,
                notification.NotificationDispatchedAt,
                notification.NotificationEventAt,
                notification.Status,
                notification.AttemptCount,
                notification.NextAttemptAt,
                notification.SentAt,
                notification.ProviderMessageId,
                notification.ErrorCode);
        }

        var telegramNotification = await context.TelegramClientNotifications.SingleAsync(item => item.Id == id);

        return new ClientNotificationSnapshot(
            telegramNotification.NotificationKey,
            telegramNotification.NotificationCommandId,
            telegramNotification.NotificationId,
            telegramNotification.NotificationDispatchedAt,
            telegramNotification.NotificationEventAt,
            telegramNotification.Status,
            telegramNotification.AttemptCount,
            telegramNotification.NextAttemptAt,
            telegramNotification.SentAt,
            telegramNotification.ProviderMessageId,
            telegramNotification.ErrorCode);
    }

    private async Task ConsumeAsync(NotificationStatusChanged result, IInterceptor? interceptor = null)
    {
        var consume = new Mock<ConsumeContext<NotificationStatusChanged>>();
        consume.SetupGet(context => context.Message).Returns(result);
        await using var context = fixture.CreateContext(interceptor);
        var consumer = new NotificationResultConsumer(
            context,
            Options.Create(new RabbitMqOptions { ApplicationId = ApplicationId, ResponseTimeoutSeconds = 30 }));
        await consumer.Consume(consume.Object);
    }

    private NotificationStatusChanged CreateResult(
        string notificationKey,
        string status,
        Guid? notificationId,
        int attemptCount,
        DateTimeOffset? occurredAt = null)
    {
        return new NotificationStatusChanged(
            Guid.NewGuid(), ApplicationId, notificationKey, notificationId, status, attemptCount,
            occurredAt ?? ResultTime, "provider-message-42", "provider-error");
    }

    private sealed class TransientSaveFailureInterceptor : SaveChangesInterceptor
    {
        public int SavedChangesAttempts { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            this.SavedChangesAttempts++;
            if (this.SavedChangesAttempts == 1)
            {
                throw new PostgresException("Тестовый конфликт после сохранения.", "ERROR", "ERROR", "40001");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed record ClientNotificationSnapshot(
        string NotificationKey,
        Guid? NotificationCommandId,
        Guid? NotificationId,
        DateTime? NotificationDispatchedAt,
        DateTime? NotificationEventAt,
        string Status,
        int AttemptCount,
        DateTime NextAttemptAt,
        DateTime? SentAt,
        string? ProviderMessageId,
        string? ErrorCode);
}
