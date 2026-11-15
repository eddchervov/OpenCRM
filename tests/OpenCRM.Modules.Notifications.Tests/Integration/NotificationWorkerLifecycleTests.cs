using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Modules.Notifications;
using OpenCRM.Modules.Notifications.Max;
using OpenCRM.Modules.Notifications.Telegram;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

[Collection(nameof(NotificationDatabaseCollection))]
public sealed class NotificationWorkerLifecycleTests(NotificationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("email")]
    [InlineData("telegram-admin")]
    [InlineData("max-client")]
    [InlineData("telegram-client")]
    public async Task AlreadyCanceledStartDoesNotResolveInfrastructure(string channel)
    {
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var options = Options.Create(new RabbitMqOptions { DispatchIntervalSeconds = 1 });
        using BackgroundService worker = channel switch
        {
            "email" => new NotificationOutboxWorker(scopes.Object, options, NullLogger<NotificationOutboxWorker>.Instance),
            "telegram-admin" => new TelegramNotificationOutboxWorker(scopes.Object, options,
                new ConfigurationBuilder().Build(), NullLogger<TelegramNotificationOutboxWorker>.Instance),
            "max-client" => new MaxNotificationWorker(scopes.Object, options, NullLogger<MaxNotificationWorker>.Instance),
            _ => new TelegramClientNotificationWorker(scopes.Object, options, NullLogger<TelegramClientNotificationWorker>.Instance)
        };
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await worker.StartAsync(cancellation.Token);
        await worker.StopAsync(default);
        scopes.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientWorkerLogsUnexpectedScopeFailureAndStops(bool telegram)
    {
        var scopes = new Mock<IServiceScopeFactory>();
        scopes.Setup(item => item.CreateScope()).Throws(new InvalidOperationException("Тестовый сбой."));
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = Options.Create(new RabbitMqOptions());
        using BackgroundService worker = telegram
            ? new TelegramClientNotificationWorker(scopes.Object, options,
                new SignalLogger<TelegramClientNotificationWorker>(signal, 2))
            : new MaxNotificationWorker(scopes.Object, options,
                new SignalLogger<MaxNotificationWorker>(signal, 2));

        await worker.StartAsync(default);
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(default);
        Assert.True(worker.ExecuteTask!.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientWorkerPropagatesCancellationWithoutLoggingItAsFailure(bool telegram)
    {
        var scopes = new Mock<IServiceScopeFactory>();
        scopes.Setup(item => item.CreateScope()).Throws(new OperationCanceledException());
        var options = Options.Create(new RabbitMqOptions());
        var maxLogger = new WorkerIterationLogger<MaxNotificationWorker>();
        var telegramLogger = new WorkerIterationLogger<TelegramClientNotificationWorker>();
        using BackgroundService worker = telegram
            ? new TelegramClientNotificationWorker(scopes.Object, options, telegramLogger)
            : new MaxNotificationWorker(scopes.Object, options, maxLogger);

        await worker.StartAsync(default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10)));
        await worker.StopAsync(default);
        Assert.Null(maxLogger.Error);
        Assert.Null(telegramLogger.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerDrainsSavedIntentAndStopsThroughPublicLifecycle(bool telegram)
    {
        var key = Guid.NewGuid().ToString("N");
        await using (var context = fixture.CreateContext())
        {
            if (telegram)
            {
                context.TelegramNotificationOutbox.Add(new TelegramNotificationOutbox { NotificationKey = key, Body = "Тест" });
                await context.SaveChangesAsync();
            }
            else
            {
                await fixture.CreateSender(context).QueueAsync("test@example.com", "Тест", "Текст", key);
            }
        }

        var endpoint = Mock.Of<ISendEndpoint>();
        var endpoints = new Mock<ISendEndpointProvider>();
        endpoints.Setup(item => item.GetSendEndpoint(It.IsAny<Uri>())).ReturnsAsync(endpoint);
        var services = new ServiceCollection();
        services.AddScoped(_ => fixture.CreateContext());
        services.AddScoped<ISendEndpointProvider>(_ => endpoints.Object);
        services.AddSingleton<IOptions<RabbitMqOptions>>(Options.Create(new RabbitMqOptions { ApplicationId = "worker-test", ResponseTimeoutSeconds = 60, SendTimeoutSeconds = 3 }));
        services.AddScoped<NotificationOutboxDispatcher>();
        services.AddScoped<TelegramNotificationOutboxDispatcher>();
        await using var provider = services.BuildServiceProvider();
        using var worker = this.CreateWorker(telegram, provider.GetRequiredService<IServiceScopeFactory>());

        await worker.StartAsync(default);
        await fixture.WaitAsync(context => telegram
            ? context.TelegramNotificationOutbox.AnyAsync(item => item.NotificationKey == key && item.NotificationDispatchedAt != null)
            : context.EmailMessages.AnyAsync(item => item.NotificationKey == key && item.NotificationDispatchedAt != null));
        await worker.StopAsync(default);
        Assert.NotNull(worker.ExecuteTask);
        Assert.True(worker.ExecuteTask.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerLogsScopeFailureAndCanBeStopped(bool telegram)
    {
        var scopes = new Mock<IServiceScopeFactory>();
        scopes.Setup(item => item.CreateScope()).Throws(new InvalidOperationException("Тестовый сбой."));
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var emailLogger = new SignalLogger<NotificationOutboxWorker>(signal, 2);
        var telegramLogger = new SignalLogger<TelegramNotificationOutboxWorker>(signal, 2);
        using var worker = this.CreateWorker(telegram, scopes.Object, emailLogger, telegramLogger);

        await worker.StartAsync(default);
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(default);
        Assert.True(worker.ExecuteTask!.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringScopeCreationTerminatesWorker(bool telegram)
    {
        using var cancellation = new CancellationTokenSource();
        var scopes = new Mock<IServiceScopeFactory>();
        scopes.Setup(item => item.CreateScope()).Callback(cancellation.Cancel).Throws(new OperationCanceledException());
        using var worker = this.CreateWorker(telegram, scopes.Object);

        await worker.StartAsync(cancellation.Token);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(default);
        scopes.Verify(item => item.CreateScope(), Times.Once);
    }

    [Fact]
    public async Task DisabledTelegramWorkerWaitsWithoutResolvingDispatcher()
    {
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var secondCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var section = new Mock<IConfigurationSection>();
        var checks = 0;
        section.SetupGet(item => item.Value).Returns(() =>
        {
            if (Interlocked.Increment(ref checks) == 2)
            {
                secondCheck.TrySetResult();
            }

            return "false";
        });
        var configuration = new Mock<IConfiguration>();
        configuration.Setup(item => item.GetSection("TelegramAdminNotifications:Enabled")).Returns(section.Object);
        using var worker = new TelegramNotificationOutboxWorker(
            scopes.Object, Options.Create(new RabbitMqOptions { DispatchIntervalSeconds = 1 }), configuration.Object,
            NullLogger<TelegramNotificationOutboxWorker>.Instance);

        await worker.StartAsync(default);
        await secondCheck.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(default);
        scopes.VerifyNoOtherCalls();
    }

    private BackgroundService CreateWorker(
        bool telegram,
        IServiceScopeFactory scopes,
        ILogger<NotificationOutboxWorker>? emailLogger = null,
        ILogger<TelegramNotificationOutboxWorker>? telegramLogger = null)
    {
        var options = Options.Create(new RabbitMqOptions { DispatchIntervalSeconds = 1 });
        if (!telegram)
        {
            return new NotificationOutboxWorker(scopes, options, emailLogger ?? NullLogger<NotificationOutboxWorker>.Instance);
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TelegramAdminNotifications:Enabled"] = "true"
        }).Build();

        return new TelegramNotificationOutboxWorker(scopes, options, configuration,
            telegramLogger ?? NullLogger<TelegramNotificationOutboxWorker>.Instance);
    }

    private sealed class SignalLogger<T>(TaskCompletionSource signal, int expectedLogCount = 1) : ILogger<T>
    {
        private int _logCount;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Assert.Equal(LogLevel.Error, logLevel);
            Assert.IsType<InvalidOperationException>(exception);
            if (Interlocked.Increment(ref this._logCount) >= expectedLogCount)
            {
                signal.TrySetResult();
            }
        }
    }
}
