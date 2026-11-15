using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Notifications;
using Notifications.Contracts;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

public sealed class NotificationRollbackTests(ClientNotificationDatabaseFixture fixture)
    : IClassFixture<ClientNotificationDatabaseFixture>, IAsyncLifetime
{
    private const string ApplicationId = "notifications-rollback-tests";

    public async Task InitializeAsync()
    {
        await using var context = fixture.CreateContext();
        await context.EmailMessageEvents.ExecuteDeleteAsync();
        await context.EmailMessages.ExecuteDeleteAsync();
        await context.TelegramNotificationOutbox.ExecuteDeleteAsync();
        await fixture.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FailedTelegramDispatchRollsBackAndRetryKeepsTheSameIdempotencyKey()
    {
        var key = Guid.NewGuid().ToString();
        await using (var setup = fixture.CreateContext())
        {
            setup.TelegramNotificationOutbox.Add(new TelegramNotificationOutbox
            {
                NotificationKey = key,
                Body = "Текст уведомления",
                NextDispatchAt = DateTime.UtcNow.AddMinutes(-1)
            });
            await setup.SaveChangesAsync();
        }

        var commands = new List<SendNotification>();
        var endpoint = new Mock<ISendEndpoint>(MockBehavior.Strict);
        endpoint.Setup(item => item.Send(It.IsAny<SendNotification>(),
                It.IsAny<IPipe<SendContext<SendNotification>>>(), It.IsAny<CancellationToken>()))
            .Callback<SendNotification, IPipe<SendContext<SendNotification>>, CancellationToken>(
                (command, _, _) => commands.Add(command))
            .Returns(Task.CompletedTask);
        var endpoints = new Mock<ISendEndpointProvider>();
        endpoints.Setup(item => item.GetSendEndpoint(It.IsAny<Uri>())).ReturnsAsync(endpoint.Object);
        var interceptor = new FailFirstSaveChangesInterceptor();
        await using var context = fixture.CreateContext(interceptor);
        var dispatcher = new TelegramNotificationOutboxDispatcher(
            context,
            endpoints.Object,
            Options.Create(new RabbitMqOptions
            {
                ApplicationId = ApplicationId,
                ResponseTimeoutSeconds = 60,
                SendTimeoutSeconds = 5
            }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchNextAsync(CancellationToken.None));

        await using (var verification = fixture.CreateContext())
        {
            var message = await verification.TelegramNotificationOutbox.SingleAsync(item => item.NotificationKey == key);
            Assert.Null(message.NotificationDispatchedAt);
        }

        Assert.True(await dispatcher.DispatchNextAsync(CancellationToken.None));

        Assert.Equal(2, commands.Count);
        Assert.All(commands, command => Assert.Equal(key, command.Request.IdempotencyKey));
        await using var finalVerification = fixture.CreateContext();
        var dispatched = await finalVerification.TelegramNotificationOutbox.SingleAsync(item => item.NotificationKey == key);
        Assert.NotNull(dispatched.NotificationDispatchedAt);
    }

    [Fact]
    public async Task FailedDeliveryRollsBackConsentAndRetryAppliesTheSameEventOnce()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        var key = Guid.NewGuid().ToString();
        string userId;
        await using (var setup = fixture.CreateContext())
        {
            var user = new ApplicationUser
            {
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                AdvertisingConsentAccepted = true,
                AdvertisingConsentAcceptedAt = DateTime.UtcNow
            };
            setup.Users.Add(user);
            var message = new EmailMessage
            {
                User = user,
                ToEmail = email,
                Subject = "Тема",
                StatusCode = EmailMessageStatusCodes.Queued,
                NotificationKey = key
            };
            setup.EmailMessages.Add(message);
            await setup.SaveChangesAsync();
            userId = user.Id;
        }

        var report = new NotificationDeliveryReported(
            Guid.NewGuid(), ApplicationId, key, EmailMessageStatusCodes.Unsubscribed, DateTimeOffset.UtcNow, email, null);
        var interceptor = new FailFirstSaveChangesInterceptor();
        await using var context = fixture.CreateContext(interceptor);
        var consumer = new NotificationDeliveryConsumer(context, Options.Create(new RabbitMqOptions { ApplicationId = ApplicationId }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(CreateConsume(report)));

        await using (var verification = fixture.CreateContext())
        {
            Assert.True((await verification.Users.SingleAsync(user => user.Id == userId)).AdvertisingConsentAccepted);
            Assert.False(await verification.EmailMessageEvents.AnyAsync(item => item.NotificationEventId == report.EventId));
            Assert.Equal(EmailMessageStatusCodes.Queued,
                (await verification.EmailMessages.SingleAsync(item => item.NotificationKey == key)).StatusCode);
        }

        await consumer.Consume(CreateConsume(report));

        await using var finalVerification = fixture.CreateContext();
        Assert.False((await finalVerification.Users.SingleAsync(user => user.Id == userId)).AdvertisingConsentAccepted);
        Assert.Equal(1, await finalVerification.EmailMessageEvents.CountAsync(item => item.NotificationEventId == report.EventId));
        Assert.Equal(EmailMessageStatusCodes.Unsubscribed,
            (await finalVerification.EmailMessages.SingleAsync(item => item.NotificationKey == key)).StatusCode);
    }

    private static ConsumeContext<NotificationDeliveryReported> CreateConsume(NotificationDeliveryReported report)
    {
        var consume = new Mock<ConsumeContext<NotificationDeliveryReported>>();
        consume.SetupGet(item => item.Message).Returns(report);

        return consume.Object;
    }

    private sealed class FailFirstSaveChangesInterceptor : SaveChangesInterceptor
    {
        private int _remainingFailures = 1;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref this._remainingFailures, 0) == 1)
            {
                throw new InvalidOperationException("Искусственный сбой сохранения.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
