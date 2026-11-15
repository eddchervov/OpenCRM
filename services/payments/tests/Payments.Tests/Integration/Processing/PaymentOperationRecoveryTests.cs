using System.Collections.Concurrent;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Payments.Contracts;
using Payments.Service.Configuration;
using Payments.Service.Domain;
using Payments.Service.Persistence;
using Payments.Service.Processing;
using Payments.Service.Providers;
using Testcontainers.PostgreSql;

namespace Payments.Tests.Integration.Processing;

public sealed class PaymentOperationRecoveryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly ConcurrentQueue<PaymentStateChanged> _events = new();
    private readonly TestTimeProvider _timeProvider = new(DateTimeOffset.UtcNow);
    private readonly RecoveryPaymentProvider _provider = new();
    private IHost? _host;

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(this._timeProvider);
        services.Configure<PaymentOptions>(options =>
        {
            options.Mode = "YooKassaTest";
            options.ApplicationId = "opencrm";
            options.MerchantAccountId = "test-shop";
            options.RetryWindowHours = 23;
        });
        services.AddDbContext<PaymentDbContext>(options =>
            options.UseNpgsql(this._postgres.GetConnectionString(),
                postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "payments")));
        services.AddSingleton(this._events);
        services.AddSingleton<IPaymentProvider>(this._provider);
        services.AddScoped<PaymentEventPublisher>();
        services.AddScoped<PaymentProcessor>();
        services.AddMassTransit(configurator =>
        {
            configurator.AddConsumer<PaymentStateChangedConsumer>();
            configurator.AddEntityFrameworkOutbox<PaymentDbContext>(options =>
            {
                options.QueryDelay = TimeSpan.FromMilliseconds(50);
                options.UsePostgres();
                options.UseBusOutbox();
            });
            configurator.UsingInMemory((context, inMemory) =>
                inMemory.ReceiveEndpoint("payments-opencrm-events", endpoint =>
                {
                    endpoint.ConfigureConsumeTopology = false;
                    endpoint.ConfigureConsumer<PaymentStateChangedConsumer>(context);
                }));
        });

        this._host = Host.CreateDefaultBuilder()
            .ConfigureServices(collection =>
            {
                foreach (var service in services)
                {
                    collection.Add(service);
                }
            })
            .Build();

        await using var scope = this._host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
        await this._host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (this._host is not null)
        {
            await this._host.StopAsync();
            this._host.Dispose();
        }

        await this._postgres.DisposeAsync();
    }

    [Fact]
    public async Task UnknownCreateOutcome_ReusesSameBodyAndKeyWithoutDuplicatePayment()
    {
        var order = this.CreateOrder();
        var operation = this.CreateOperation(order, OperationKind.CreatePayment);
        this._provider.CreatePaymentResult = this.CreateProviderPayment(order, "provider-payment-recovered", "succeeded", true, 0);
        this._provider.ThrowAfterAcceptingFirstPayment = true;
        await this.SaveAsync(order, operation);

        await this.ProcessOperationAsync(operation.Id);
        DateTimeOffset? firstAttemptAt;
        await using (var database = this.CreateDatabase())
        {
            var failedAttempt = await database.ProviderOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id);
            Assert.Equal(OperationStatus.OutcomeUnknown, failedAttempt.Status);
            Assert.Null(failedAttempt.ProviderObjectId);
            firstAttemptAt = failedAttempt.FirstAttemptAt;
        }

        await this.MakeOperationDueAsync(operation.Id);
        await this.ProcessOperationAsync(operation.Id);

        Assert.Equal(2, this._provider.CreatePaymentCalls);
        Assert.Equal(1, this._provider.AcceptedPaymentCount);
        Assert.Equal(this._provider.CreatePaymentBodies[0], this._provider.CreatePaymentBodies[1]);
        Assert.Equal(this._provider.CreatePaymentKeys[0], this._provider.CreatePaymentKeys[1]);

        await using var verificationDatabase = this.CreateDatabase();
        var resolved = await verificationDatabase.ProviderOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id);
        var resolvedOrder = await verificationDatabase.PaymentOrders.AsNoTracking().SingleAsync(item => item.Id == order.Id);
        Assert.Equal(OperationStatus.Resolved, resolved.Status);
        Assert.Equal("provider-payment-recovered", resolved.ProviderObjectId);
        Assert.Equal(firstAttemptAt, resolved.FirstAttemptAt);
        Assert.Equal(PaymentStatus.Succeeded, resolvedOrder.Status);
    }

    [Fact]
    public async Task ExpiredIdempotenceWindow_EntersManualReviewWithoutPosting()
    {
        var order = this.CreateOrder();
        var operation = this.CreateOperation(order, OperationKind.CreatePayment);
        operation.Status = OperationStatus.OutcomeUnknown;
        operation.FirstAttemptAt = this._timeProvider.GetUtcNow().AddHours(-23).AddSeconds(-1);
        operation.AttemptCount = 1;
        operation.NextAttemptAt = this._timeProvider.GetUtcNow().AddSeconds(-1);
        this._provider.CreatePaymentResult = this.CreateProviderPayment(order, "provider-payment-late", "succeeded", true, 0);
        await this.SaveAsync(order, operation);

        await this.ProcessOperationAsync(operation.Id);

        Assert.Equal(0, this._provider.CreatePaymentCalls);
        await using var database = this.CreateDatabase();
        var savedOperation = await database.ProviderOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id);
        var savedOrder = await database.PaymentOrders.AsNoTracking().SingleAsync(item => item.Id == order.Id);
        Assert.Equal(OperationStatus.ManualReview, savedOperation.Status);
        Assert.Equal("provider_idempotence_window_expired", savedOperation.LastError);
        Assert.Equal(savedOperation.LastError, savedOrder.LastError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrExpiredAbsoluteDeadline_BlocksFirstProviderPost(bool missing)
    {
        var order = this.CreateOrder();
        var operation = this.CreateOperation(order, OperationKind.CreatePayment);
        operation.PostBefore = missing ? null : this._timeProvider.GetUtcNow().AddSeconds(-1);
        await this.SaveAsync(order, operation);

        await this.ProcessOperationAsync(operation.Id);

        Assert.Equal(0, this._provider.CreatePaymentCalls);
        await using var database = this.CreateDatabase();
        var savedOperation = await database.ProviderOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id);
        Assert.Equal(OperationStatus.ManualReview, savedOperation.Status);
        Assert.Equal(missing ? "provider_post_deadline_missing" : "provider_post_deadline_expired", savedOperation.LastError);
        Assert.Contains(await database.PaymentAuditEntries.ToListAsync(), entry => entry.Code == savedOperation.LastError);
    }

    [Fact]
    public async Task KnownProviderObject_CanBeReadAfterAbsoluteDeadlineWithoutAnotherPost()
    {
        var order = this.CreateOrder(PaymentStatus.Pending, "provider-known-after-deadline");
        var operation = this.CreateOperation(order, OperationKind.CreatePayment);
        operation.Status = OperationStatus.AwaitingProvider;
        operation.ProviderObjectId = order.ProviderPaymentId;
        operation.PostBefore = this._timeProvider.GetUtcNow().AddHours(-1);
        operation.FirstAttemptAt = this._timeProvider.GetUtcNow().AddHours(-24);
        this._provider.PaymentResult = this.CreateProviderPayment(order, order.ProviderPaymentId!, "succeeded", true, 0);
        await this.SaveAsync(order, operation);

        await this.ProcessOperationAsync(operation.Id);

        Assert.Equal(0, this._provider.CreatePaymentCalls);
        Assert.Equal(1, this._provider.GetPaymentCalls);
        await using var database = this.CreateDatabase();
        Assert.Equal(OperationStatus.Resolved, (await database.ProviderOperations.SingleAsync()).Status);
        Assert.Equal(PaymentStatus.Succeeded, (await database.PaymentOrders.SingleAsync()).Status);
    }

    [Fact]
    public async Task ConfirmedRefundId_IsReusedAfterPaymentReadFailure()
    {
        var order = this.CreateOrder(PaymentStatus.Succeeded, "provider-payment-refund");
        var creationOperation = this.CreateOperation(order, OperationKind.CreatePayment);
        creationOperation.Status = OperationStatus.Resolved;
        creationOperation.ProviderObjectId = order.ProviderPaymentId;
        var refundOperation = this.CreateOperation(order, OperationKind.Refund, 1000);
        this._provider.RefundResult = new ProviderRefund("provider-refund-durable", order.ProviderPaymentId!, "succeeded", 1000, "RUB");
        this._provider.PaymentResult = this.CreateProviderPayment(order, order.ProviderPaymentId!, "succeeded", true, 1000);
        this._provider.FailNextPaymentRead = true;
        await this.SaveAsync(order, creationOperation, refundOperation);

        await this.ProcessOperationAsync(refundOperation.Id);

        await using (var database = this.CreateDatabase())
        {
            var waiting = await database.ProviderOperations.AsNoTracking().SingleAsync(item => item.Id == refundOperation.Id);
            var orderAfterFailure = await database.PaymentOrders.AsNoTracking().SingleAsync(item => item.Id == order.Id);
            Assert.Equal(OperationStatus.AwaitingProvider, waiting.Status);
            Assert.Equal("provider-refund-durable", waiting.ProviderObjectId);
            Assert.Equal(0, orderAfterFailure.RefundedAmountMinor);
            Assert.Equal(1000, waiting.AmountMinor);
        }

        await this.MakeOperationDueAsync(refundOperation.Id);
        await this.ProcessOperationAsync(refundOperation.Id);

        Assert.Equal(1, this._provider.CreateRefundCalls);
        Assert.Equal(1, this._provider.GetRefundCalls);
        Assert.Equal(2, this._provider.GetPaymentCalls);
        await using var verificationDatabase = this.CreateDatabase();
        var resolvedRefund = await verificationDatabase.ProviderOperations.AsNoTracking()
            .SingleAsync(item => item.Id == refundOperation.Id);
        var refundedOrder = await verificationDatabase.PaymentOrders.AsNoTracking().SingleAsync(item => item.Id == order.Id);
        Assert.Equal(OperationStatus.Resolved, resolvedRefund.Status);
        Assert.Equal("provider-refund-durable", resolvedRefund.ProviderObjectId);
        Assert.Equal(1000, refundedOrder.RefundedAmountMinor);
    }

    [Fact]
    public async Task ReconciliationReadFailure_SchedulesBackoffInsteadOfImmediatePolling()
    {
        var order = this.CreateOrder(PaymentStatus.Pending, "provider-payment-reconciliation");
        this._provider.FailNextPaymentRead = true;
        await this.SaveAsync(order);

        await this.ReconcilePaymentAsync(order.Id);

        await using var database = this.CreateDatabase();
        var savedOrder = await database.PaymentOrders.AsNoTracking().SingleAsync(item => item.Id == order.Id);
        Assert.Equal(1, savedOrder.ReconciliationFailureCount);
        Assert.Equal("temporary_payment_read_failure", savedOrder.LastError);
        Assert.True(savedOrder.NextCheckAt > this._timeProvider.GetUtcNow());
    }

    [Fact]
    public async Task DuplicateLatePaymentWebhooks_DoNotRegressTerminalStateOrPublishTwice()
    {
        var order = this.CreateOrder(PaymentStatus.Succeeded, "provider-payment-terminal");
        var creationOperation = this.CreateOperation(order, OperationKind.CreatePayment);
        creationOperation.Status = OperationStatus.Resolved;
        creationOperation.ProviderObjectId = order.ProviderPaymentId;
        this._provider.PaymentResult = this.CreateProviderPayment(order, order.ProviderPaymentId!, "pending", false, 0);
        var firstWebhook = this.CreateWebhook("payment.succeeded", order.ProviderPaymentId!);
        await this.SaveAsync(order, creationOperation, firstWebhook);

        await this.ReconcilePaymentAsync(order.Id);
        var conflictEvent = await this.ReadEventAsync();
        Assert.Equal(PaymentStatus.Succeeded.ToString(), conflictEvent.PaymentStatus);
        this._provider.PaymentResult = this.CreateProviderPayment(order, order.ProviderPaymentId!, "succeeded", true, 0);
        await this.ProcessWebhookAsync(firstWebhook.Id);
        await this.ProcessWebhookAsync(firstWebhook.Id);
        await Task.Delay(200);

        Assert.Equal(order.PurchaseId, conflictEvent.PurchaseId);
        Assert.Empty(this._events);
        await using var database = this.CreateDatabase();
        var savedOrder = await database.PaymentOrders.AsNoTracking().SingleAsync(item => item.Id == order.Id);
        var savedWebhook = await database.WebhookNotifications.AsNoTracking().SingleAsync(item => item.Id == firstWebhook.Id);
        Assert.Equal(PaymentStatus.Succeeded, savedOrder.Status);
        Assert.Equal(1, savedOrder.Version);
        Assert.NotNull(savedWebhook.ProcessedAt);
    }

    [Fact]
    public async Task ChangedMerchantAccountBlocksPaymentBeforeProviderCall()
    {
        var order = this.CreateOrder();
        order.MerchantAccountId = "previous-merchant";
        var operation = this.CreateOperation(order, OperationKind.CreatePayment);
        await this.SaveAsync(order, operation);

        await this.ProcessOperationAsync(operation.Id);

        await using var database = this.CreateDatabase();
        var savedOperation = await database.ProviderOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id);
        Assert.Equal(0, this._provider.CreatePaymentCalls);
        Assert.Equal(OperationStatus.ManualReview, savedOperation.Status);
        Assert.Equal("payment_merchant_configuration_mismatch", savedOperation.LastError);
    }

    private PaymentOrder CreateOrder(PaymentStatus status = PaymentStatus.Pending, string? providerPaymentId = null)
    {
        var now = this._timeProvider.GetUtcNow();

        return new PaymentOrder
        {
            Id = Guid.NewGuid(),
            ApplicationId = "opencrm",
            PurchaseId = Guid.NewGuid(),
            RequestHash = Guid.NewGuid().ToString("N"),
            AmountMinor = 5000,
            Currency = "RUB",
            Description = "Подписка opencrm.example.invalid",
            ReturnUrl = "https://opencrm.example.invalid/payment/complete",
            BuyerId = "buyer-recovery",
            BeneficiaryId = "organization-recovery",
            MerchantAccountId = "test-shop",
            IsTest = true,
            ProviderPaymentId = providerPaymentId,
            ConfirmationUrl = providerPaymentId is null ? null : "https://yookassa.ru/checkout/payments/" + providerPaymentId,
            Status = status,
            CreatedAt = now,
            NextCheckAt = now
        };
    }

    private ProviderOperation CreateOperation(PaymentOrder order, OperationKind kind, long amountMinor = 5000) => new()
    {
        Id = Guid.NewGuid(),
        PaymentOrder = order,
        PaymentOrderId = order.Id,
        CommandId = Guid.NewGuid(),
        Kind = kind,
        ExecutionMode = "YooKassaTest",
        AmountMinor = amountMinor,
        RequestBody = "{\"immutable\":\"request\"}",
        IdempotenceKey = Guid.NewGuid().ToString("D"),
        Status = OperationStatus.Prepared,
        CreatedAt = this._timeProvider.GetUtcNow(),
        UpdatedAt = this._timeProvider.GetUtcNow(),
        PostBefore = this._timeProvider.GetUtcNow().AddHours(23),
        NextAttemptAt = this._timeProvider.GetUtcNow()
    };

    private WebhookNotification CreateWebhook(string eventType, string objectId)
    {
        var now = this._timeProvider.GetUtcNow();

        return new WebhookNotification
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            ObjectId = objectId,
            ReceivedAt = now,
            NextAttemptAt = now
        };
    }

    private ProviderPayment CreateProviderPayment(
        PaymentOrder order,
        string paymentId,
        string status,
        bool paid,
        long refundedAmountMinor) => new(
        paymentId,
        status,
        order.AmountMinor,
        order.Currency,
        paid,
        order.IsTest,
        refundedAmountMinor,
        order.ConfirmationUrl,
        order.Id.ToString(),
        order.MerchantAccountId);

    private async Task SaveAsync(PaymentOrder order, params object[] records)
    {
        await using var database = this.CreateDatabase();
        database.PaymentOrders.Add(order);
        foreach (var record in records)
        {
            switch (record)
            {
                case ProviderOperation operation:
                    operation.PaymentOrder = order;
                    operation.PaymentOrderId = order.Id;
                    database.ProviderOperations.Add(operation);
                    break;
                case WebhookNotification notification:
                    database.WebhookNotifications.Add(notification);
                    break;
            }
        }

        await database.SaveChangesAsync();
    }

    private PaymentDbContext CreateDatabase() => new(new DbContextOptionsBuilder<PaymentDbContext>()
        .UseNpgsql(this._postgres.GetConnectionString(),
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "payments")).Options);

    private async Task ProcessOperationAsync(Guid operationId)
    {
        await using var scope = this._host!.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PaymentProcessor>()
            .ProcessOperationAsync(operationId, CancellationToken.None);
    }

    private async Task ProcessWebhookAsync(Guid webhookId)
    {
        await using var scope = this._host!.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PaymentProcessor>()
            .ProcessWebhookAsync(webhookId, CancellationToken.None);
    }

    private async Task ReconcilePaymentAsync(Guid paymentOrderId)
    {
        await using var scope = this._host!.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PaymentProcessor>()
            .ReconcilePaymentAsync(paymentOrderId, CancellationToken.None);
    }

    private async Task MakeOperationDueAsync(Guid operationId)
    {
        await using var database = this.CreateDatabase();
        await database.ProviderOperations.Where(item => item.Id == operationId)
            .ExecuteUpdateAsync(properties => properties.SetProperty(item => item.NextAttemptAt, this._timeProvider.GetUtcNow()));
    }

    private async Task<PaymentStateChanged> ReadEventAsync()
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeout)
        {
            if (this._events.TryDequeue(out var paymentState))
            {
                return paymentState;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Изменение состояния платежа не поступило в очередь.");
    }

    private sealed class RecoveryPaymentProvider : IPaymentProvider
    {
        private readonly ConcurrentDictionary<string, ProviderPayment> _acceptedPayments = new();
        private readonly ConcurrentQueue<string> _createPaymentBodies = new();
        private readonly ConcurrentQueue<string> _createPaymentKeys = new();
        private int _acceptedPaymentCount;
        private int _createPaymentCalls;
        private int _createRefundCalls;
        private int _getPaymentCalls;
        private int _getRefundCalls;

        public ProviderPayment? CreatePaymentResult { get; set; }
        public ProviderPayment? PaymentResult { get; set; }
        public ProviderRefund? RefundResult { get; set; }
        public bool ThrowAfterAcceptingFirstPayment { get; set; }
        public bool FailNextPaymentRead { get; set; }
        public int AcceptedPaymentCount => Volatile.Read(ref this._acceptedPaymentCount);
        public int CreatePaymentCalls => Volatile.Read(ref this._createPaymentCalls);
        public int CreateRefundCalls => Volatile.Read(ref this._createRefundCalls);
        public int GetPaymentCalls => Volatile.Read(ref this._getPaymentCalls);
        public int GetRefundCalls => Volatile.Read(ref this._getRefundCalls);
        public IReadOnlyList<string> CreatePaymentBodies => this._createPaymentBodies.ToArray();
        public IReadOnlyList<string> CreatePaymentKeys => this._createPaymentKeys.ToArray();

        public string BuildPaymentRequest(PaymentOrder order) => "{}";

        public string BuildRefundRequest(PaymentOrder order, long amountMinor, string reason) => "{}";

        public Task<ProviderPayment> CreatePaymentAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this._createPaymentCalls);
            this._createPaymentBodies.Enqueue(requestBody);
            this._createPaymentKeys.Enqueue(idempotenceKey);
            var acceptedPayment = this._acceptedPayments.GetOrAdd(idempotenceKey, _ =>
            {
                Interlocked.Increment(ref this._acceptedPaymentCount);

                return this.CreatePaymentResult ?? throw new InvalidOperationException("Не задан ответ CreatePayment.");
            });
            if (this.ThrowAfterAcceptingFirstPayment && this.CreatePaymentCalls == 1)
            {
                throw new PaymentProviderException("response_lost_after_acceptance");
            }

            return Task.FromResult(acceptedPayment);
        }

        public Task<ProviderPayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this._getPaymentCalls);
            if (this.FailNextPaymentRead)
            {
                this.FailNextPaymentRead = false;

                throw new PaymentProviderException("temporary_payment_read_failure");
            }

            return Task.FromResult(this.PaymentResult ?? this._acceptedPayments.Values.Single());
        }

        public Task<ProviderRefund> CreateRefundAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this._createRefundCalls);

            return Task.FromResult(this.RefundResult ?? throw new InvalidOperationException("Не задан ответ CreateRefund."));
        }

        public Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this._getRefundCalls);

            return Task.FromResult(this.RefundResult ?? throw new InvalidOperationException("Не задан ответ GetRefund."));
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        private DateTimeOffset _currentTime = currentTime;

        public override DateTimeOffset GetUtcNow() => this._currentTime;

    }

    private sealed class PaymentStateChangedConsumer(ConcurrentQueue<PaymentStateChanged> events)
        : IConsumer<PaymentStateChanged>
    {
        public Task Consume(ConsumeContext<PaymentStateChanged> context)
        {
            events.Enqueue(context.Message);

            return Task.CompletedTask;
        }
    }
}
