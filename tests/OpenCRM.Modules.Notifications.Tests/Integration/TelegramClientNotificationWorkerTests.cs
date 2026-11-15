using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Notifications;
using OpenCRM.Modules.Notifications.Telegram;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using Notifications.Contracts;
using Npgsql;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

public sealed class TelegramClientNotificationWorkerTests(ClientNotificationDatabaseFixture fixture)
    : IClassFixture<ClientNotificationDatabaseFixture>, IAsyncLifetime
{
    private WorkerIterationScopeFactory _iterationScopes = null!;
    private WorkerIterationLogger<TelegramClientNotificationWorker> _iterationLogger = null!;

    public async Task InitializeAsync()
    {
        await using var context = fixture.CreateContext();
        await context.TelegramClientNotifications.ExecuteDeleteAsync();
        await context.OrganizationTelegramBots.ExecuteDeleteAsync();
        await fixture.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("Organizations")]
    [InlineData("Vehicles")]
    public async Task ConcurrentDeletionAfterClaimSkipsNotificationWithoutSending(string table)
    {
        await this.SeedAsync();
        var deletion = new ConcurrentDeleteInterceptor(table, async () =>
        {
            await using var context = fixture.CreateContext();
            if (table == "Organizations")
            {
                await context.Organizations.ExecuteDeleteAsync();
            }
            else
            {
                await context.Vehicles.ExecuteDeleteAsync();
            }
        });
        var endpoints = new Mock<ISendEndpointProvider>(MockBehavior.Strict);
        using var services = this.CreateServices(deletion);

        await this.RunIterationAsync(this.CreateWorker(services, endpoints.Object));

        await using var verification = fixture.CreateContext();
        Assert.True(deletion.HasDeleted);
        Assert.Equal("skipped", (await verification.TelegramClientNotifications.SingleAsync()).Status);
        endpoints.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EmptyQueueDoesNotCallNotificationEndpoint()
    {
        var endpoints = new Mock<ISendEndpointProvider>(MockBehavior.Strict);
        using var services = this.CreateServices();

        await this.RunIterationAsync(this.CreateWorker(services, endpoints.Object));

        endpoints.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FreePlanSkipsQueuedTelegramNotificationWithoutPublishingIt()
    {
        await this.SeedAsync();
        var endpoints = new Mock<ISendEndpointProvider>(MockBehavior.Strict);
        using var services = this.CreateServices(hasClientMessagingFeature: false);

        await this.RunIterationAsync(this.CreateWorker(services, endpoints.Object));

        await using var context = fixture.CreateContext();
        var notification = await context.TelegramClientNotifications.SingleAsync();
        Assert.Equal("skipped", notification.Status);
        Assert.Equal("client-messaging-pro-required", notification.ErrorCode);
        endpoints.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("missing-bot")]
    [InlineData("missing-client")]
    [InlineData("disabled-client")]
    [InlineData("deleted-client")]
    [InlineData("changed-recipient")]
    [InlineData("missing-maintenance")]
    [InlineData("not-connected")]
    [InlineData("reconnected")]
    [InlineData("inactive-organization")]
    [InlineData("deleted-organization")]
    [InlineData("archived-organization")]
    [InlineData("changed-vehicle-client")]
    [InlineData("newer-notification")]
    [InlineData("ready-disabled")]
    [InlineData("booking-disabled")]
    [InlineData("not-ready")]
    [InlineData("handed-over")]
    [InlineData("not-cancelled")]
    [InlineData("completed-booking")]
    [InlineData("completed-changed-booking")]
    public async Task ChangedEligibilitySkipsOldNotificationWithoutCallingTransport(string change)
    {
        await this.SeedAsync();
        await using (var context = fixture.CreateContext())
        {
            var item = await context.TelegramClientNotifications.SingleAsync();
            var bot = await context.OrganizationTelegramBots.SingleAsync();
            var client = await context.OrganizationClients.SingleAsync();
            var organization = await context.Organizations.SingleAsync();
            var maintenance = await context.Maintenance.SingleAsync();
            var vehicle = await context.Vehicles.SingleAsync();
            switch (change)
            {
                case "missing-bot": context.OrganizationTelegramBots.Remove(bot); break;
                case "missing-client": item.ClientId = Guid.NewGuid(); break;
                case "disabled-client": client.TelegramNotificationsEnabled = false; break;
                case "deleted-client": client.IsDeleted = true; break;
                case "changed-recipient": client.TelegramChatId++; break;
                case "missing-maintenance": item.MaintenanceId = Guid.NewGuid(); break;
                case "not-connected": client.TelegramConnectedAt = null; break;
                case "reconnected": client.TelegramConnectedAt = item.CreatedAt.AddMinutes(1); break;
                case "inactive-organization": organization.IsActive = false; break;
                case "deleted-organization": organization.IsDeleted = true; break;
                case "archived-organization": organization.IsArchived = true; break;
                case "changed-vehicle-client": vehicle.OrganizationClientId = null; break;
                case "newer-notification":
                    context.TelegramClientNotifications.Add(new TelegramClientNotification
                    {
                        OrganizationId = item.OrganizationId, ClientId = item.ClientId, MaintenanceId = item.MaintenanceId,
                        NotificationKey = Guid.NewGuid().ToString(), ChatId = item.ChatId,
                        NextAttemptAt = DateTime.UtcNow.AddDays(1)
                    });
                    break;
                case "ready-disabled": item.EventCode = "ready"; bot.NotifyReady = false; break;
                case "booking-disabled": bot.NotifyBooking = false; break;
                case "not-ready": item.EventCode = "ready"; break;
                case "handed-over":
                    item.EventCode = "ready";
                    maintenance.Status = MaintenanceStatus.Completed;
                    maintenance.ReadyForHandoverAt = DateTime.UtcNow;
                    maintenance.HandedOverAt = DateTime.UtcNow;
                    break;
                case "not-cancelled": item.EventCode = "booking_cancelled"; break;
                case "completed-booking": maintenance.Status = MaintenanceStatus.Completed; break;
                case "completed-changed-booking": item.EventCode = "booking_changed"; maintenance.Status = MaintenanceStatus.Completed; break;
            }

            await context.SaveChangesAsync();
        }

        var endpoints = new Mock<ISendEndpointProvider>(MockBehavior.Strict);
        using var services = this.CreateServices();
        await this.RunIterationAsync(this.CreateWorker(services, endpoints.Object));
        await using var verification = fixture.CreateContext();
        Assert.Equal("skipped", (await verification.TelegramClientNotifications.OrderBy(item => item.Id).FirstAsync()).Status);
        endpoints.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("booking_created")]
    [InlineData("booking_changed")]
    [InlineData("ready")]
    [InlineData("booking_cancelled")]
    public async Task EligibleNotificationIsSentInItsCurrentMaintenanceState(string eventCode)
    {
        await this.SeedAsync();
        await using (var context = fixture.CreateContext())
        {
            var notification = await context.TelegramClientNotifications.SingleAsync();
            var maintenance = await context.Maintenance.SingleAsync();
            notification.EventCode = eventCode;
            maintenance.Status = eventCode switch
            {
                "ready" => MaintenanceStatus.Completed,
                "booking_cancelled" => MaintenanceStatus.Cancelled,
                _ => MaintenanceStatus.InProgress
            };
            if (eventCode == "ready")
            {
                maintenance.ReadyForHandoverAt = DateTime.UtcNow.AddMinutes(-1);
            }

            await context.SaveChangesAsync();
        }

        var (endpoints, _, sent) = TelegramClientNotificationWorkerTests.CreateEndpoint();
        using var services = this.CreateServices();
        await this.RunIterationAsync(this.CreateWorker(services, endpoints.Object));
        Assert.IsType<SendNotification>(Assert.Single(sent));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchedIntentQueriesStatusEvenIfBotWasDisabled(bool delayDisposal)
    {
        await this.SeedAsync();
        await using (var context = fixture.CreateContext())
        {
            var item = await context.TelegramClientNotifications.SingleAsync();
            item.NotificationDispatchedAt = DateTime.UtcNow;
            (await context.OrganizationTelegramBots.SingleAsync()).IsEnabled = false;
            await context.SaveChangesAsync();
        }

        var (endpoints, endpoint, _) = TelegramClientNotificationWorkerTests.CreateEndpoint();
        endpoint.Setup(target => target.Send(It.IsAny<GetNotificationStatus>(),
            It.IsAny<IPipe<SendContext<GetNotificationStatus>>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        using var services = this.CreateServices(delayDisposal: delayDisposal);

        await this.RunIterationAsync(this.CreateWorker(services, endpoints.Object));
        endpoint.Verify(target => target.Send(It.IsAny<GetNotificationStatus>(),
            It.IsAny<IPipe<SendContext<GetNotificationStatus>>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClaimRetriesAndSendsOnce(bool shouldFailAfterSave)
    {
        var key = await this.SeedAsync();
        var interceptor = new TelegramTransientClaimFailureInterceptor(shouldFailAfterSave);
        var (endpoints, _, sent) = CreateEndpoint();
        using var services = this.CreateServices(interceptor);

        await this.RunIterationAsync(this.CreateWorker(services, endpoints.Object));

        Assert.Equal(3, interceptor.SaveChangesAttempts);
        var command = Assert.IsType<SendNotification>(Assert.Single(sent));
        Assert.Equal(NotificationChannels.TelegramClient, command.Request.Channel);
        Assert.Equal(key, command.Request.IdempotencyKey);
        await using var context = fixture.CreateContext();
        var notification = await context.TelegramClientNotifications.SingleAsync();
        Assert.Equal("processing", notification.Status);
        Assert.NotNull(notification.NotificationDispatchedAt);
        Assert.NotNull(notification.NotificationCommandId);
        Assert.Equal(1, await context.TelegramClientNotifications.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrokerFailureRetainsKeyAndAllowsLaterRetry(bool shouldFailOnSend)
    {
        var key = await this.SeedAsync();
        var failingEndpoints = new Mock<ISendEndpointProvider>(MockBehavior.Strict);
        if (shouldFailOnSend)
        {
            var failingEndpoint = new Mock<ISendEndpoint>(MockBehavior.Strict);
            failingEndpoint.Setup(target => target.Send(
                    It.IsAny<SendNotification>(),
                    It.IsAny<IPipe<SendContext<SendNotification>>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Тестовый сбой отправки."));
            failingEndpoints.Setup(provider => provider.GetSendEndpoint(It.IsAny<Uri>()))
                .ReturnsAsync(failingEndpoint.Object);
        }
        else
        {
            failingEndpoints.Setup(provider => provider.GetSendEndpoint(It.IsAny<Uri>()))
                .ThrowsAsync(new IOException("Тестовый сбой брокера."));
        }
        using var failingServices = this.CreateServices();

        await Assert.ThrowsAsync<IOException>(
            () => this.RunIterationAsync(this.CreateWorker(failingServices, failingEndpoints.Object)));

        await using (var afterFailure = fixture.CreateContext())
        {
            var persisted = await afterFailure.TelegramClientNotifications.SingleAsync();
            Assert.Equal("processing", persisted.Status);
            Assert.Null(persisted.NotificationDispatchedAt);
            Assert.Null(persisted.NotificationCommandId);
            Assert.Equal(key, persisted.NotificationKey);
            persisted.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
            await afterFailure.SaveChangesAsync();
        }

        var (retryEndpoints, _, sent) = CreateEndpoint();
        using var retryServices = this.CreateServices();

        await this.RunIterationAsync(this.CreateWorker(retryServices, retryEndpoints.Object));

        var command = Assert.IsType<SendNotification>(Assert.Single(sent));
        Assert.Equal(key, command.Request.IdempotencyKey);
        await using var context = fixture.CreateContext();
        var notification = await context.TelegramClientNotifications.SingleAsync();
        Assert.Equal("processing", notification.Status);
        Assert.NotNull(notification.NotificationDispatchedAt);
        Assert.NotNull(notification.NotificationCommandId);
    }

    [Fact]
    public async Task LostDispatchSaveReplaysSameKey()
    {
        var key = await this.SeedAsync();
        var interceptor = new TelegramLostDispatchSaveInterceptor();
        var (firstEndpoints, _, firstSent) = CreateEndpoint();
        using var firstServices = this.CreateServices(interceptor);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => this.RunIterationAsync(this.CreateWorker(firstServices, firstEndpoints.Object)));

        Assert.Equal(key, Assert.IsType<SendNotification>(Assert.Single(firstSent)).Request.IdempotencyKey);
        await using (var afterFailure = fixture.CreateContext())
        {
            var persisted = await afterFailure.TelegramClientNotifications.SingleAsync();
            Assert.Equal("processing", persisted.Status);
            Assert.Null(persisted.NotificationDispatchedAt);
            Assert.Null(persisted.NotificationCommandId);
            Assert.Equal(key, persisted.NotificationKey);
            persisted.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
            await afterFailure.SaveChangesAsync();
        }

        var (retryEndpoints, _, retrySent) = CreateEndpoint();
        using var retryServices = this.CreateServices();

        await this.RunIterationAsync(this.CreateWorker(retryServices, retryEndpoints.Object));

        var replayed = Assert.IsType<SendNotification>(Assert.Single(retrySent));
        Assert.Equal(key, replayed.Request.IdempotencyKey);
        await using var context = fixture.CreateContext();
        var notification = await context.TelegramClientNotifications.SingleAsync();
        Assert.Equal("processing", notification.Status);
        Assert.NotNull(notification.NotificationDispatchedAt);
    }

    private async Task<string> SeedAsync()
    {
        await fixture.CreateNotificationAsync();
        await using var setup = fixture.CreateContext();
        var max = await setup.MaxClientNotifications.SingleAsync();
        var client = await setup.OrganizationClients.SingleAsync(x => x.Id == max.ClientId);
        client.TelegramNotificationsEnabled = true;
        client.TelegramChatId = 12345;
        client.TelegramConnectedAt = DateTime.UtcNow.AddDays(-1);
        setup.OrganizationTelegramBots.Add(new OrganizationTelegramBot
        {
            OrganizationId = max.OrganizationId,
            IsEnabled = true,
            Username ="test-bot",
            ProtectedToken = "test-token",
            ProtectedWebhookSecret = "test-secret",
            NotifyBooking = true
        });
        var key = "telegram-client:" + Guid.NewGuid().ToString("N");
        setup.TelegramClientNotifications.Add(new TelegramClientNotification
        {
            OrganizationId = max.OrganizationId,
            ClientId = max.ClientId,
            MaintenanceId = max.MaintenanceId,
            ChatId = 12345,
            NotificationKey = key,
            EventCode = "booking_created",
            Body = "Тест",
            Status = "queued",
            NextAttemptAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow
        });
        await setup.SaveChangesAsync();

        return key;
    }

    private ServiceProvider CreateServices(
        IInterceptor? interceptor = null,
        bool delayDisposal = false,
        bool hasClientMessagingFeature = true)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => fixture.CreateContext(interceptor, delayDisposal));
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), Helpers.FeatureCodes.ClientMessaging))
            .ReturnsAsync(hasClientMessagingFeature);
        services.AddScoped(_ => subscriptions.Object);

        return services.BuildServiceProvider();
    }

    private TelegramClientNotificationWorker CreateWorker(IServiceProvider services, ISendEndpointProvider endpoints)
    {
        var endpointScopes = new EndpointServiceScopeFactory(services.GetRequiredService<IServiceScopeFactory>(), endpoints);
        this._iterationScopes = new WorkerIterationScopeFactory(endpointScopes);
        this._iterationLogger = new WorkerIterationLogger<TelegramClientNotificationWorker>();

        return new TelegramClientNotificationWorker(
            this._iterationScopes,
            Options.Create(new RabbitMqOptions
            {
                ApplicationId = "telegram-worker-test",
                ResponseTimeoutSeconds = 30,
                SendTimeoutSeconds = 5
            }),
            this._iterationLogger);
    }

    private async Task RunIterationAsync(TelegramClientNotificationWorker worker)
    {
        using (worker)
        {
            await worker.StartAsync(default);
            await this._iterationScopes.Completed.WaitAsync(TimeSpan.FromSeconds(15));
            await worker.StopAsync(default);
        }

        if (this._iterationLogger.Error is { } error)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    private static (Mock<ISendEndpointProvider> Endpoints, Mock<ISendEndpoint> Endpoint, List<object> Sent)
        CreateEndpoint()
    {
        var sent = new List<object>();
        var endpoint = new Mock<ISendEndpoint>(MockBehavior.Strict);
        endpoint.Setup(target => target.Send(
                It.IsAny<SendNotification>(),
                It.IsAny<IPipe<SendContext<SendNotification>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<SendNotification, IPipe<SendContext<SendNotification>>, CancellationToken>(
                (message, _, _) => sent.Add(message));
        var endpoints = new Mock<ISendEndpointProvider>(MockBehavior.Strict);
        endpoints.Setup(provider => provider.GetSendEndpoint(It.IsAny<Uri>()))
            .ReturnsAsync(endpoint.Object);

        return (endpoints, endpoint, sent);
    }

    private sealed class EndpointServiceScopeFactory(
        IServiceScopeFactory scopes,
        ISendEndpointProvider endpoints) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new EndpointServiceScope(scopes.CreateScope(), endpoints);
    }

    private sealed class EndpointServiceScope(IServiceScope scope, ISendEndpointProvider endpoints)
        : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider => new EndpointServiceProvider(scope.ServiceProvider, endpoints);

        public void Dispose() => scope.Dispose();

        public async ValueTask DisposeAsync()
        {
            if (scope is IAsyncDisposable asyncScope)
            {
                await asyncScope.DisposeAsync();
                return;
            }

            scope.Dispose();
        }
    }

    private sealed class EndpointServiceProvider(
        IServiceProvider services,
        ISendEndpointProvider endpoints) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(ISendEndpointProvider)
            ? endpoints
            : services.GetService(serviceType);
    }

    private sealed class TelegramTransientClaimFailureInterceptor(bool shouldFailAfterSave) : SaveChangesInterceptor
    {
        public int SaveChangesAttempts { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            this.SaveChangesAttempts++;
            if (!shouldFailAfterSave && this.SaveChangesAttempts == 1)
            {
                throw new PostgresException("Тестовый конфликт сериализации.", "ERROR", "ERROR", "40001");
            }

            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (shouldFailAfterSave && this.SaveChangesAttempts == 1)
            {
                throw new PostgresException("Тестовый конфликт после сохранения.", "ERROR", "ERROR", "40001");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class TelegramLostDispatchSaveInterceptor : SaveChangesInterceptor
    {
        public int SaveChangesAttempts { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            this.SaveChangesAttempts++;
            if (this.SaveChangesAttempts == 2)
            {
                throw new DbUpdateException("Тестовый сбой сохранения после отправки.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
