using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Contracts;
using Payments.Service.Configuration;
using Payments.Service.Domain;
using Payments.Service.Messaging;
using Payments.Service.Processing;
using Payments.Service.Persistence;
using Payments.Service.Providers;

namespace Payments.Tests.Integration;

public sealed class PaymentServiceTests(PaymentServiceFixture fixture) : IClassFixture<PaymentServiceFixture>
{
    [Fact]
    public async Task MigrationMetadataMatchesModelAndCanCreateAnEmptyDatabase()
    {
        await using var database = fixture.CreateDatabase();
        var migrations = await database.Database.GetAppliedMigrationsAsync();

        Assert.Equal(4, migrations.Count());
        Assert.False(database.Database.HasPendingModelChanges());
        var script = database.GetService<IMigrator>().GenerateScript(migrations.Last(), "0");
        Assert.Contains("DROP TABLE", script);
        Assert.Contains("payments", script);
    }

    [Fact]
    public async Task DownMigrationIsRejectedAfterCommandPreparedAnOperation()
    {
        var command = PaymentServiceFixture.Command();
        await using var scope = fixture.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var commands = new PaymentCommandService(database,
            scope.ServiceProvider.GetRequiredService<IPaymentProvider>(),
            scope.ServiceProvider.GetRequiredService<PaymentEventPublisher>(),
            scope.ServiceProvider.GetRequiredService<IOptions<PaymentOptions>>(), TimeProvider.System,
            scope.ServiceProvider.GetRequiredService<ILogger<PaymentCommandService>>());

        await commands.AcceptAsync(command, CancellationToken.None);
        var operation = await database.ProviderOperations.SingleAsync(item => item.CommandId == command.CommandId);
        Assert.NotNull(operation.PostBefore);

        var migrator = database.GetService<IMigrator>();
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            migrator.MigrateAsync("20261025140000_HardenPaymentExecutionAndAudit"));

        Assert.Contains("Нельзя удалить", exception.Message,
            StringComparison.Ordinal);
        await migrator.MigrateAsync();
        Assert.Equal(4, (await database.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task DownMigrationPreservesVerifiedSuccessTime()
    {
        var command = PaymentServiceFixture.Command();
        await using var scope = fixture.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var commands = scope.ServiceProvider.GetRequiredService<PaymentCommandService>();
        await commands.AcceptAsync(command, CancellationToken.None);
        await database.PaymentOrders.Where(order => order.PurchaseId == command.PurchaseId)
            .ExecuteUpdateAsync(update => update.SetProperty(order => order.SucceededAt, DateTimeOffset.UtcNow));
        var migrator = database.GetService<IMigrator>();
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            migrator.MigrateAsync("20261025180000_AddProviderOperationPostBefore"));
        Assert.Contains("Нельзя удалить время подтверждения", exception.Message, StringComparison.Ordinal);
        Assert.Equal(4, (await database.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task DisabledModeRejectsCommandWithoutCreatingAnOperation()
    {
        var command = PaymentServiceFixture.Command();
        await using var scope = fixture.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var options = Options.Create(new PaymentOptions
        {
            Mode = "Disabled",
            ApplicationId = "opencrm",
            MerchantAccountId = "12345",

            AllowedReturnUrls = ["https://opencrm.example.invalid/payment/complete"]
        });
        var commands = new PaymentCommandService(database,
            scope.ServiceProvider.GetRequiredService<IPaymentProvider>(),
            scope.ServiceProvider.GetRequiredService<PaymentEventPublisher>(),
            options,
            TimeProvider.System,
            scope.ServiceProvider.GetRequiredService<ILogger<PaymentCommandService>>());

        await commands.AcceptAsync(command, CancellationToken.None);
        await commands.AcceptAsync(command, CancellationToken.None);
        Assert.True(await database.CommandReceipts.AnyAsync(item => item.CommandId == command.CommandId));
        Assert.False(await database.PaymentOrders.AnyAsync(order => order.PurchaseId == command.PurchaseId));
        Assert.False(await database.ProviderOperations.AnyAsync(item => item.PaymentOrder.PurchaseId == command.PurchaseId));
    }

    [Fact]
    public async Task CommandRequiresValidDeadlineAndMerchantMetadata()
    {
        var command = PaymentServiceFixture.Command();
        await using var scope = fixture.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var options = Microsoft.Extensions.Options.Options.Create(new PaymentOptions
        {
            Mode = "Disabled",
            ApplicationId = "opencrm",
            MerchantAccountId = command.MerchantAccountId,
            AllowedReturnUrls = ["https://opencrm.example.invalid/payment/complete"]
        });
        var commands = new PaymentCommandService(database,
            scope.ServiceProvider.GetRequiredService<IPaymentProvider>(),
            scope.ServiceProvider.GetRequiredService<PaymentEventPublisher>(), options, TimeProvider.System,
            scope.ServiceProvider.GetRequiredService<ILogger<PaymentCommandService>>());
        var invalidCommands = new[]
        {
            command with { IssuedAt = default },
            command with { NotAfter = default },
            command with { MerchantAccountId = string.Empty },
            command with { SchemaVersion = 1 },
            command with { IssuedAt = DateTimeOffset.UtcNow.AddMinutes(6) },
            command with { NotAfter = command.IssuedAt.AddHours(24) }
        };

        foreach (var invalidCommand in invalidCommands)
        {
            var exception = await Assert.ThrowsAsync<PaymentCommandException>(() =>
                commands.AcceptAsync(invalidCommand with { CommandId = Guid.NewGuid(), PurchaseId = Guid.NewGuid() },
                    CancellationToken.None));
            Assert.Equal("invalid_payment", exception.Code);
        }

        Assert.False(await database.CommandReceipts.AnyAsync());
        Assert.False(await database.PaymentOrders.AnyAsync());
    }

    [Fact]
    public async Task ReturnUrlMustContainOnlyTheMatchingPurchaseId()
    {
        var command = PaymentServiceFixture.Command();
        await using var scope = fixture.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var options = Options.Create(new PaymentOptions
        {
            Mode = "Disabled",
            ApplicationId = "opencrm",
            MerchantAccountId = "disabled",
            AllowedReturnUrls = ["https://opencrm.example.invalid/payment/complete"]
        });
        var commands = new PaymentCommandService(database,
            scope.ServiceProvider.GetRequiredService<IPaymentProvider>(),
            scope.ServiceProvider.GetRequiredService<PaymentEventPublisher>(),
            options, TimeProvider.System, scope.ServiceProvider.GetRequiredService<ILogger<PaymentCommandService>>());

        var exception = await Assert.ThrowsAsync<PaymentCommandException>(() =>
            commands.AcceptAsync(command with { ReturnUrl = command.ReturnUrl + "&redirect=https://evil.test" },
                CancellationToken.None));

        Assert.Equal("invalid_payment", exception.Code);
        Assert.False(await database.PaymentOrders.AnyAsync(order => order.PurchaseId == command.PurchaseId));
    }

    [Fact]
    public async Task ChangedPurchaseAndChangedCommandCannotReplaceAcceptedAmount()
    {
        var command = PaymentServiceFixture.Command();
        await fixture.SendAsync(command);
        await fixture.WaitAsync(database => database.PaymentOrders.AnyAsync(order => order.PurchaseId == command.PurchaseId));
        var changedCommandMessageId = await fixture.SendAsync(command with { AmountMinor = command.AmountMinor + 1 });
        var changedPurchaseMessageId = await fixture.SendAsync(command with { CommandId = Guid.NewGuid(), AmountMinor = command.AmountMinor + 2 });

        await fixture.WaitAsync(async database => await database.Set<MassTransit.EntityFrameworkCoreIntegration.InboxState>()
            .CountAsync(item => item.Consumed != null && (item.MessageId == changedCommandMessageId
                || item.MessageId == changedPurchaseMessageId)) == 2);

        await using var database = fixture.CreateDatabase();
        var order = await database.PaymentOrders.Include(item => item.Operations)
            .SingleAsync(item => item.PurchaseId == command.PurchaseId);
        Assert.Equal(command.AmountMinor, order.AmountMinor);
        Assert.Single(order.Operations);
    }

    [Fact]
    public async Task ProviderAcceptedPaymentIsDeduplicatedAfterDatabaseRollbackAndCommandRedelivery()
    {
        var command = PaymentServiceFixture.Command();
        var provider = new DeduplicatingPaymentProvider();
        string firstBody;
        string firstKey;
        Guid operationId;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            await using var rollback = await database.Database.BeginTransactionAsync();
            var commands = new PaymentCommandService(database, provider,
                scope.ServiceProvider.GetRequiredService<PaymentEventPublisher>(),
                scope.ServiceProvider.GetRequiredService<IOptions<PaymentOptions>>(), TimeProvider.System,
                scope.ServiceProvider.GetRequiredService<ILogger<PaymentCommandService>>());

            await commands.AcceptAsync(command, CancellationToken.None);
            var operation = await database.ProviderOperations.SingleAsync(item => item.CommandId == command.CommandId);
            firstBody = operation.RequestBody;
            firstKey = operation.IdempotenceKey;
            operationId = operation.Id;

            _ = await provider.CreatePaymentAsync(firstBody, firstKey, CancellationToken.None);
            Assert.Equal(1, provider.AcceptedPaymentCount);
            Assert.Single(provider.PostCalls);
            await rollback.RollbackAsync();
        }

        await using (var restoredDatabase = fixture.CreateDatabase())
        {
            Assert.False(await restoredDatabase.CommandReceipts.AnyAsync(item => item.CommandId == command.CommandId));
            Assert.False(await restoredDatabase.PaymentOrders.AnyAsync(item => item.PurchaseId == command.PurchaseId));
            Assert.False(await restoredDatabase.ProviderOperations.AnyAsync(item => item.CommandId == command.CommandId));
        }

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<PaymentOptions>>();
            var publisher = scope.ServiceProvider.GetRequiredService<PaymentEventPublisher>();
            var commands = new PaymentCommandService(database, provider, publisher, options, TimeProvider.System,
                scope.ServiceProvider.GetRequiredService<ILogger<PaymentCommandService>>());
            await commands.AcceptAsync(command, CancellationToken.None);
            var operation = await database.ProviderOperations.SingleAsync(item => item.CommandId == command.CommandId);

            Assert.Equal(operationId, operation.Id);
            Assert.Equal(firstBody, operation.RequestBody);
            Assert.Equal(firstKey, operation.IdempotenceKey);
            Assert.Equal(command.NotAfter, operation.PostBefore);

            var processor = new PaymentProcessor(database, provider, publisher, options, TimeProvider.System,
                scope.ServiceProvider.GetRequiredService<ILogger<PaymentProcessor>>());
            await processor.ProcessOperationAsync(operation.Id, CancellationToken.None);

            var completedOperation = await database.ProviderOperations.AsNoTracking()
                .SingleAsync(item => item.CommandId == command.CommandId);
            var completedOrder = await database.PaymentOrders.AsNoTracking()
                .SingleAsync(item => item.PurchaseId == command.PurchaseId);
            Assert.Equal(OperationStatus.Resolved, completedOperation.Status);
            Assert.Equal(provider.AcceptedPayment.Id, completedOrder.ProviderPaymentId);
            Assert.Equal(provider.AcceptedPayment.Id, completedOperation.ProviderObjectId);
            Assert.Equal(1, provider.AcceptedPaymentCount);
            Assert.Equal(2, provider.PostCalls.Count);
            Assert.All(provider.PostCalls, post =>
            {
                Assert.Equal(firstBody, post.Body);
                Assert.Equal(firstKey, post.IdempotenceKey);
            });

            var now = DateTimeOffset.UtcNow;
            var expiredCommand = PaymentServiceFixture.Command() with
            {
                IssuedAt = now.AddHours(-24),
                NotAfter = now.AddHours(-1)
            };
            await commands.AcceptAsync(expiredCommand, CancellationToken.None);
            Assert.False(await database.ProviderOperations.AnyAsync(item => item.CommandId == expiredCommand.CommandId));
            Assert.True(await database.CommandReceipts.AnyAsync(item => item.CommandId == expiredCommand.CommandId));
            Assert.Equal(2, provider.PostCalls.Count);
            Assert.Equal(1, provider.AcceptedPaymentCount);
        }
    }

    [Fact]
    public async Task ExpiredUnknownCommandIsRecordedWithoutCreatingPaymentOperation()
    {
        var now = DateTimeOffset.UtcNow;
        var command = PaymentServiceFixture.Command() with
        {
            IssuedAt = now.AddHours(-24),
            NotAfter = now.AddHours(-1)
        };
        await using var scope = fixture.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var commands = new PaymentCommandService(database,
            scope.ServiceProvider.GetRequiredService<IPaymentProvider>(),
            scope.ServiceProvider.GetRequiredService<PaymentEventPublisher>(),
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaymentOptions>>(),
            TimeProvider.System,
            scope.ServiceProvider.GetRequiredService<ILogger<PaymentCommandService>>());

        await commands.AcceptAsync(command, CancellationToken.None);
        await commands.AcceptAsync(command, CancellationToken.None);

        Assert.True(await database.CommandReceipts.AnyAsync(item => item.CommandId == command.CommandId));
        Assert.False(await database.PaymentOrders.AnyAsync(item => item.PurchaseId == command.PurchaseId));
        Assert.False(await database.ProviderOperations.AnyAsync(item => item.CommandId == command.CommandId));
    }

    [Fact]
    public async Task ConcurrentRefundsCannotReserveMoreThanPaymentAmount()
    {
        var command = PaymentServiceFixture.Command();
        await fixture.SendAsync(command);
        await fixture.WaitAsync(database => database.PaymentOrders.AnyAsync(order => order.PurchaseId == command.PurchaseId));
        await using (var database = fixture.CreateDatabase())
        {
            var order = await database.PaymentOrders.Include(item => item.Operations)
                .SingleAsync(item => item.PurchaseId == command.PurchaseId);
            order.Status = PaymentStatus.Succeeded;
            order.ProviderPaymentId = Guid.NewGuid().ToString();
            order.Operations[0].Status = OperationStatus.Resolved;
            await database.SaveChangesAsync();
        }

        var first = new RequestRefund(Guid.NewGuid(), "opencrm", command.PurchaseId, 100_000,
            "Тест возврата", "operator", IssuedAt: command.IssuedAt, NotAfter: command.NotAfter,
            MerchantAccountId: command.MerchantAccountId);
        var messageIds = await Task.WhenAll(fixture.SendAsync(first), fixture.SendAsync(first with { CommandId = Guid.NewGuid() }));
        await fixture.WaitAsync(async database => await database.Set<MassTransit.EntityFrameworkCoreIntegration.InboxState>()
            .CountAsync(item => item.Consumed != null && messageIds.Contains(item.MessageId)) == 2);

        await using var resultDatabase = fixture.CreateDatabase();
        var refunds = await resultDatabase.ProviderOperations.Where(operation => operation.PaymentOrder.PurchaseId == command.PurchaseId
            && operation.Kind == OperationKind.Refund).ToListAsync();

        Assert.Equal(100_000, Assert.Single(refunds).AmountMinor);
        Assert.Equal(OperationStatus.Prepared, refunds[0].Status);
    }

    [Fact]
    public async Task ResultWaitsInRabbitMqUntilFutureApplicationConnects()
    {
        var command = PaymentServiceFixture.Command();
        var messageId = await fixture.SendAsync(command);
        await fixture.WaitAsync(database => database.Set<MassTransit.EntityFrameworkCoreIntegration.InboxState>()
            .AnyAsync(item => item.MessageId == messageId && item.Delivered != null));

        var received = new TaskCompletionSource<PaymentStateChanged>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = Host.CreateDefaultBuilder().ConfigureServices(services => services.AddMassTransit(registration =>
            registration.UsingRabbitMq((_, bus) =>
            {
                bus.Host(fixture.RabbitHost, fixture.RabbitPort, "payments", host =>
                {
                    host.Username("payments");
                    host.Password("payments-integration-tests-only");
                });
                bus.ReceiveEndpoint("payments-opencrm-events", endpoint =>
                {
                    endpoint.ConfigureConsumeTopology = false;
                    endpoint.Handler<PaymentStateChanged>(context =>
                    {
                        if (context.Message.PurchaseId == command.PurchaseId)
                        {
                            received.TrySetResult(context.Message);
                        }

                        return Task.CompletedTask;
                    });
                    endpoint.Handler<PaymentCommandRejected>(_ => Task.CompletedTask);
                });
            }))).Build();

        await listener.StartAsync();
        try
        {
            var state = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("Prepared", state.CreationStatus);
            Assert.Equal(command.AmountMinor, state.AmountMinor);
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    [Fact]
    public async Task RejectedProductCommandIsPublishedToConfiguredEventsQueue()
    {
        var received = new TaskCompletionSource<PaymentCommandRejected>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var database = fixture.CreateDatabase();
        using var host = Host.CreateDefaultBuilder().ConfigureServices(services =>
        {
            services.AddLogging();
            services.AddSingleton(database);
            services.AddSingleton<IOptions<PaymentOptions>>(Options.Create(new PaymentOptions
            {
                Mode = "Disabled",
                ApplicationId = "sample-crm",
                MerchantAccountId = "12345",
                AllowedReturnUrls = ["https://opencrm.example.invalid/payment/complete"]
            }));
            services.AddSingleton<TimeProvider>(TimeProvider.System);
            services.AddSingleton<IPaymentProvider, DisabledPaymentProvider>();
            services.AddScoped<PaymentEventPublisher>();
            services.AddScoped<PaymentCommandService>();
            services.AddMassTransit(registration =>
            {
                registration.AddConsumer<PaymentCommandConsumer>();
                registration.UsingInMemory((context, transport) =>
                {
                    transport.ReceiveEndpoint("payments-sample-crm-commands", endpoint =>
                        endpoint.ConfigureConsumer<PaymentCommandConsumer>(context));
                    transport.ReceiveEndpoint("payments-sample-crm-events", endpoint =>
                        endpoint.Handler<PaymentCommandRejected>(message =>
                        {
                            received.TrySetResult(message.Message);
                            return Task.CompletedTask;
                        }));
                });
            });
        }).Build();

        await host.StartAsync();
        try
        {
            var bus = host.Services.GetRequiredService<IBus>();
            var endpoint = await bus.GetSendEndpoint(new Uri("queue:payments-sample-crm-commands"));
            await endpoint.Send(PaymentServiceFixture.Command() with { ApplicationId = "sample-crm", AmountMinor = 0 });

            var rejection = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("sample-crm", rejection.ApplicationId);
            Assert.Equal("invalid_payment", rejection.Reason);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task PrivateReadRequiresServiceKeyAndLimitsApplication()
    {
        var command = PaymentServiceFixture.Command();
        await fixture.SendAsync(command);
        await fixture.WaitAsync(database => database.PaymentOrders.AnyAsync(order => order.PurchaseId == command.PurchaseId));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/payments/opencrm/{command.PurchaseId}");
        request.Headers.Add("X-Payments-Key", "wrong");
        using var denied = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var allowed = await fixture.Client.GetAsync($"/internal/payments/opencrm/{command.PurchaseId}");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var body = await allowed.Content.ReadAsStringAsync();
        Assert.DoesNotContain("IdempotenceKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RequestBody", body, StringComparison.OrdinalIgnoreCase);
        using var foreign = await fixture.Client.GetAsync($"/internal/payments/another/{command.PurchaseId}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task WebhookIsDurableAndDuplicateDoesNotProvePayment()
    {
        var objectId = Guid.NewGuid().ToString();
        var notification = new { type = "notification", @event = "payment.succeeded", @object = new { id = objectId } };
        using var first = await fixture.Client.PostAsJsonAsync("/webhooks/yookassa", notification);
        using var second = await fixture.Client.PostAsJsonAsync("/webhooks/yookassa", notification);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        await using var database = fixture.CreateDatabase();
        var saved = await database.WebhookNotifications.SingleAsync(item => item.ObjectId == objectId);
        Assert.Null(saved.ProcessedAt);
        Assert.False(await database.PaymentOrders.AnyAsync(order => order.ProviderPaymentId == objectId));
    }

    private sealed class DeduplicatingPaymentProvider : IPaymentProvider
    {
        private readonly Dictionary<string, (string Body, ProviderPayment Payment)> _acceptedPayments = [];

        public List<(string Body, string IdempotenceKey)> PostCalls { get; } = [];
        public int AcceptedPaymentCount => this._acceptedPayments.Count;
        public ProviderPayment AcceptedPayment => Assert.Single(this._acceptedPayments.Values).Payment;

        public string BuildPaymentRequest(PaymentOrder order) => JsonSerializer.Serialize(new
        {
            amountMinor = order.AmountMinor,
            currency = order.Currency,
            metadata = new
            {
                paymentOrderId = order.Id,
                purchaseId = order.PurchaseId,
                applicationId = order.ApplicationId,
                merchantAccountId = order.MerchantAccountId
            }
        });

        public string BuildRefundRequest(PaymentOrder order, long amountMinor, string reason) =>
            JsonSerializer.Serialize(new { paymentId = order.ProviderPaymentId, amountMinor, reason });

        public Task<ProviderPayment> CreatePaymentAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken)
        {
            this.PostCalls.Add((requestBody, idempotenceKey));
            if (this._acceptedPayments.TryGetValue(idempotenceKey, out var accepted))
            {
                if (accepted.Body != requestBody)
                {
                    throw new InvalidOperationException("Один ключ идемпотентности получил другое тело.");
                }

                return Task.FromResult(accepted.Payment);
            }

            using var document = JsonDocument.Parse(requestBody);
            var root = document.RootElement;
            var metadata = root.GetProperty("metadata");
            var payment = new ProviderPayment(
                $"provider-{idempotenceKey}",
                "pending",
                root.GetProperty("amountMinor").GetInt64(),
                root.GetProperty("currency").GetString()!,
                false,
                true,
                0,
                "https://yookassa.ru/checkout/test",
                metadata.GetProperty("paymentOrderId").GetGuid().ToString("D"),
                metadata.GetProperty("merchantAccountId").GetString()!);
            this._acceptedPayments.Add(idempotenceKey, (requestBody, payment));

            return Task.FromResult(payment);
        }

        public Task<ProviderPayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderRefund> CreateRefundAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task ReadinessIsUnavailableWhenBusStopsButLivenessRemainsHealthy()
    {
        var bus = fixture.Services.GetRequiredService<IBusControl>();
        try
        {
            await bus.StopAsync();
            using var ready = await fixture.Client.GetAsync("/health/ready");
            using var live = await fixture.Client.GetAsync("/health/live");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());
        }
        finally
        {
            await bus.StartAsync();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            using var recovered = await fixture.Client.GetAsync("/health/ready", timeout.Token);
            if (recovered.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(50, timeout.Token);
        }
    }

    [Fact]
    public async Task HealthEndpointsRespondOnRunningIsolatedService()
    {
        using var live = await fixture.Client.GetAsync("/health/live");
        using var ready = await fixture.Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }
}
