using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Notifications;
using OpenCRM.Testing;
using Notifications.Contracts;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

[Collection(nameof(NotificationDatabaseCollection))]
public sealed class NotificationDispatcherRecoveryTests(NotificationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchedIntentPollsStatusWithSameKeyAndAwaitsTransactionCleanup(bool telegram)
    {
        await using var original = fixture.CreateContext();
        var options = (DbContextOptions<OpenCRMContext>)original.GetService<IDbContextOptions>();
        await using var context = new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>(options)
            .ReplaceService<IRelationalTransactionFactory, AsyncDisposalTransactionFactory>().Options);
        var key = Guid.NewGuid().ToString();
        if (telegram)
        {
            context.TelegramNotificationOutbox.Add(new TelegramNotificationOutbox { NotificationKey = key, Body = "Текст" });
            await context.SaveChangesAsync();
        }
        else
        {
            await fixture.CreateSender(context).QueueAsync("test@example.com", "Тема", "Текст", key);
        }

        var commands = new List<object>();
        var endpoint = new Mock<ISendEndpoint>(MockBehavior.Strict);
        endpoint.Setup(item => item.Send(It.IsAny<SendNotification>(),
                It.IsAny<IPipe<SendContext<SendNotification>>>(), It.IsAny<CancellationToken>()))
            .Callback<SendNotification, IPipe<SendContext<SendNotification>>, CancellationToken>((command, _, _) => commands.Add(command))
            .Returns(Task.CompletedTask);
        endpoint.Setup(item => item.Send(It.IsAny<GetNotificationStatus>(),
                It.IsAny<IPipe<SendContext<GetNotificationStatus>>>(), It.IsAny<CancellationToken>()))
            .Callback<GetNotificationStatus, IPipe<SendContext<GetNotificationStatus>>, CancellationToken>((command, _, _) => commands.Add(command))
            .Returns(Task.CompletedTask);
        var endpoints = new Mock<ISendEndpointProvider>();
        endpoints.Setup(item => item.GetSendEndpoint(It.IsAny<Uri>())).ReturnsAsync(endpoint.Object);
        var rabbitOptions = Options.Create(fixture.Options);
        Task<bool> Dispatch() => telegram
            ? new TelegramNotificationOutboxDispatcher(context, endpoints.Object, rabbitOptions).DispatchNextAsync(default)
            : new NotificationOutboxDispatcher(context, endpoints.Object, rabbitOptions).DispatchNextAsync(default);

        Assert.True(await Dispatch());
        Assert.Equal(key, Assert.IsType<SendNotification>(Assert.Single(commands)).Request.IdempotencyKey);
        context.ChangeTracker.Clear();
        if (telegram)
        {
            var item = await context.TelegramNotificationOutbox.SingleAsync(item => item.NotificationKey == key);
            Assert.NotNull(item.NotificationDispatchedAt);
            item.NextDispatchAt = DateTime.UtcNow.AddSeconds(-1);
        }
        else
        {
            var item = await context.EmailMessages.SingleAsync(item => item.NotificationKey == key);
            Assert.NotNull(item.NotificationDispatchedAt);
            item.NextDispatchAt = DateTime.UtcNow.AddSeconds(-1);
        }

        await context.SaveChangesAsync();
        Assert.True(await Dispatch());
        Assert.Equal(key, Assert.IsType<GetNotificationStatus>(commands[1]).IdempotencyKey);
        Assert.Equal(2, commands.Count);
        Assert.True(((AsyncDisposalTransactionFactory)context.GetService<IRelationalTransactionFactory>()).HasDisposedTransaction);
        Assert.Null(context.Database.CurrentTransaction);
    }
}
