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

public sealed class PaymentProcessorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly ConcurrentQueue<PaymentStateChanged> _events = new();
    private readonly ConcurrentQueue<PaymentOperationStateChanged> _operationEvents = new();
    private readonly TestTimeProvider _timeProvider = new(DateTimeOffset.UtcNow);
    private readonly RecordingPaymentProvider _provider;
    private IHost? _host;
    private Guid _paymentOrderId;
    private Guid _purchaseId;
    private string _providerPaymentId = "provider-payment-1";
    private ProviderPayment? _providerPayment;

    public PaymentProcessorTests()
    {
        this._provider = new RecordingPaymentProvider(() => this._providerPayment ?? this.CreateProviderPayment());
    }

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
        services.AddSingleton(this._operationEvents);
        services.AddSingleton<IPaymentProvider>(this._provider);
        services.AddScoped<PaymentEventPublisher>();
        services.AddScoped<PaymentProcessor>();
        services.AddMassTransit(configurator =>
        {
            configurator.AddConsumer<PaymentStateChangedConsumer>();
            configurator.AddConsumer<PaymentOperationStateChangedConsumer>();
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
                    endpoint.ConfigureConsumer<PaymentOperationStateChangedConsumer>(context);
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
        var database = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await database.Database.MigrateAsync();
        this._paymentOrderId = Guid.NewGuid();
        this._purchaseId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var order = new PaymentOrder
        {
            Id = this._paymentOrderId,
            ApplicationId = "opencrm",
            PurchaseId = this._purchaseId,
            RequestHash = "test-hash",
            AmountMinor = 12500,
            Currency = "RUB",
            Description = "Подписка opencrm.example.invalid",
            ReturnUrl = "https://opencrm.example.invalid/payment/complete",
            BuyerId = "buyer-1",
            BeneficiaryId = "beneficiary-1",
            MerchantAccountId = "test-shop",
            IsTest = true,
            ProviderPaymentId = this._providerPaymentId,
            ConfirmationUrl = "https://yookassa.ru/checkout/payments/provider-payment-1",
            Status = PaymentStatus.Pending,
            CreatedAt = now,
            NextCheckAt = now.AddMinutes(-1),
            Operations =
            [
                new ProviderOperation
                {
                    Id = Guid.NewGuid(),
                    Kind = OperationKind.CreatePayment,
                    ExecutionMode = "YooKassaTest",
                    CommandId = Guid.NewGuid(),
                    AmountMinor = 12500,
                    RequestBody = "{}",
                    IdempotenceKey = Guid.NewGuid().ToString("D"),
                    Status = OperationStatus.Resolved,
                    ProviderObjectId = this._providerPaymentId,
                    CreatedAt = now,
                    UpdatedAt = now
                }
            ]
        };
        database.PaymentOrders.Add(order);
        await database.SaveChangesAsync();
        await this._host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (this._host != null)
        {
            await this._host.StopAsync();
            this._host.Dispose();
        }

        await this._postgres.DisposeAsync();
    }

    [Fact]
    public async Task Reconciliation_UpdatesVerifiedPaymentAndPersistsAddressedEvent()
    {
        await using (var scope = this._host!.Services.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<PaymentProcessor>();
            await processor.ReconcilePaymentAsync(this._paymentOrderId, CancellationToken.None);
        }

        var delivered = await this.ReadEventAsync();

        Assert.Equal(this._purchaseId, delivered.PurchaseId);
        Assert.Equal(this._paymentOrderId, delivered.PaymentOrderId);
        Assert.Equal(1, delivered.AggregateVersion);
        Assert.Equal(nameof(PaymentStatus.Succeeded), delivered.PaymentStatus);
        Assert.Equal(nameof(OperationStatus.Resolved), delivered.CreationStatus);
        Assert.Equal(0, delivered.RefundedAmountMinor);
        Assert.Equal(this._providerPaymentId, delivered.ProviderPaymentId);

        await using var verificationScope = this._host.Services.CreateAsyncScope();
        var order = await verificationScope.ServiceProvider.GetRequiredService<PaymentDbContext>()
            .PaymentOrders.AsNoTracking().SingleAsync(item => item.Id == this._paymentOrderId);
        Assert.Equal(PaymentStatus.Succeeded, order.Status);
        Assert.Equal(1, order.Version);
    }

    [Fact]
    public async Task ReplayedReconciliation_DoesNotPublishAnotherStateChange()
    {
        await using (var scope = this._host!.Services.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<PaymentProcessor>();
            await processor.ReconcilePaymentAsync(this._paymentOrderId, CancellationToken.None);
            await processor.ReconcilePaymentAsync(this._paymentOrderId, CancellationToken.None);
        }

        _ = await this.ReadEventAsync();
        await Task.Delay(200);

        Assert.Empty(this._events);
        await using var verificationScope = this._host!.Services.CreateAsyncScope();
        var order = await verificationScope.ServiceProvider.GetRequiredService<PaymentDbContext>()
            .PaymentOrders.AsNoTracking().SingleAsync(item => item.Id == this._paymentOrderId);
        Assert.Equal(1, order.Version);
    }

    [Fact]
    public async Task Operation_DoesNotPostAfterLeaseExpiresBeforeProviderCall()
    {
        var operationId = Guid.NewGuid();
        var now = this._timeProvider.GetUtcNow();
        await using (var setupScope = this._host!.Services.CreateAsyncScope())
        {
            var database = setupScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            database.ProviderOperations.Add(new ProviderOperation
            {
                Id = operationId,
                PaymentOrderId = this._paymentOrderId,
                CommandId = Guid.NewGuid(),
                Kind = OperationKind.Refund,
                ExecutionMode = "YooKassaTest",
                AmountMinor = 1000,
                RequestBody = "{}",
                IdempotenceKey = Guid.NewGuid().ToString("D"),
                Status = OperationStatus.Prepared,
                CreatedAt = now,
                UpdatedAt = now,
                PostBefore = now.AddHours(23),
                NextAttemptAt = now.AddMinutes(-1)
            });
            await database.SaveChangesAsync();
        }

        this._timeProvider.AdvanceOnRead(2, TimeSpan.FromMinutes(3));
        await using (var scope = this._host.Services.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<PaymentProcessor>();
            await processor.ProcessOperationAsync(operationId, CancellationToken.None);
        }

        Assert.Equal(0, this._provider.CreateRefundCalls);
        await using var verificationScope = this._host.Services.CreateAsyncScope();
        var operation = await verificationScope.ServiceProvider.GetRequiredService<PaymentDbContext>()
            .ProviderOperations.AsNoTracking().SingleAsync(item => item.Id == operationId);
        Assert.Equal(OperationStatus.Sending, operation.Status);
        Assert.True(operation.LeaseUntil < this._timeProvider.GetUtcNow());
        Assert.NotNull(operation.LeaseToken);
    }

    [Fact]
    public async Task ReconciliationMismatch_UsesBackoffAndWritesAuditEntry()
    {
        this._providerPayment = this.CreateProviderPayment() with { AmountMinor = 1 };
        await using (var scope = this._host!.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PaymentProcessor>()
                .ReconcilePaymentAsync(this._paymentOrderId, CancellationToken.None);
        }

        await using var verificationScope = this._host!.Services.CreateAsyncScope();
        var database = verificationScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var order = await database.PaymentOrders.AsNoTracking().SingleAsync(item => item.Id == this._paymentOrderId);
        var audit = await database.PaymentAuditEntries.SingleAsync(item => item.PaymentOrderId == this._paymentOrderId);
        Assert.Equal("provider_payment_mismatch", order.LastError);
        Assert.Equal(1, order.ReconciliationFailureCount);
        Assert.True(order.NextCheckAt > this._timeProvider.GetUtcNow());
        Assert.Equal("provider_payment_mismatch", audit.Code);
        Assert.Equal("ProviderResponseMismatch", audit.ErrorType);
    }

    [Fact]
    public async Task DueReconciliation_DetectsOldExternalRefundAndStopsAfterFullRefund()
    {
        var now = this._timeProvider.GetUtcNow();
        this._providerPayment = this.CreateProviderPayment() with { RefundedAmountMinor = 1000 };
        await using (var setupScope = this._host!.Services.CreateAsyncScope())
        {
            var database = setupScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var order = await database.PaymentOrders.SingleAsync(item => item.Id == this._paymentOrderId);
            order.Status = PaymentStatus.Succeeded;
            order.CreatedAt = now.AddDays(-35);
            order.SucceededAt = now.AddDays(-35);
            order.NextCheckAt = now.AddMinutes(-1);
            await database.SaveChangesAsync();
        }

        await using (var scope = this._host!.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PaymentProcessor>()
                .ProcessDueReconciliationsAsync(CancellationToken.None);
        }

        await using (var verificationScope = this._host!.Services.CreateAsyncScope())
        {
            var database = verificationScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var order = await database.PaymentOrders.AsNoTracking().SingleAsync(item => item.Id == this._paymentOrderId);
            Assert.Equal(1000, order.RefundedAmountMinor);
            Assert.InRange(order.NextCheckAt.UtcTicks, now.AddDays(7).UtcTicks - 20, now.AddDays(7).UtcTicks + 20);
        }

        this._providerPayment = this.CreateProviderPayment() with { RefundedAmountMinor = 12500 };
        await using (var setupScope = this._host!.Services.CreateAsyncScope())
        {
            var database = setupScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var order = await database.PaymentOrders.SingleAsync(item => item.Id == this._paymentOrderId);
            order.NextCheckAt = now.AddMinutes(-1);
            await database.SaveChangesAsync();
        }

        await using (var scope = this._host!.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PaymentProcessor>()
                .ProcessDueReconciliationsAsync(CancellationToken.None);
        }

        await using var fullRefundScope = this._host!.Services.CreateAsyncScope();
        var fullyRefundedOrder = await fullRefundScope.ServiceProvider.GetRequiredService<PaymentDbContext>()
            .PaymentOrders.AsNoTracking().SingleAsync(item => item.Id == this._paymentOrderId);
        Assert.Equal(12500, fullyRefundedOrder.RefundedAmountMinor);
        Assert.Equal(DateTimeOffset.MaxValue, fullyRefundedOrder.NextCheckAt);
    }

    [Fact]
    public async Task CanceledRefund_EmitsOutcomeAndPreservesPaymentReconciliation()
    {
        this._provider.RefundStatus = "canceled";
        this._provider.RefundAmount = 1000;
        var scheduledCheck = this._timeProvider.GetUtcNow().AddMinutes(20);
        var operation = this.CreateRefundOperation(1000);
        await this.SaveRefundAsync(operation, scheduledCheck);

        await using (var scope = this._host!.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PaymentProcessor>()
                .ProcessOperationAsync(operation.Id, CancellationToken.None);
        }

        var outcome = await this.ReadOperationEventAsync();
        await using var verificationScope = this._host!.Services.CreateAsyncScope();
        var database = verificationScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var order = await database.PaymentOrders.AsNoTracking().SingleAsync(item => item.Id == this._paymentOrderId);
        var result = await database.ProviderOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id);
        Assert.Equal(operation.Id, outcome.OperationId);
        Assert.Equal("Resolved", outcome.Status);
        Assert.Equal("refund_canceled", outcome.ErrorCode);
        Assert.Equal(OperationStatus.Resolved, result.Status);
        Assert.Equal("refund_canceled", result.LastError);
        Assert.NotEqual(DateTimeOffset.MaxValue, order.NextCheckAt);
        Assert.Equal("refund_canceled", await database.PaymentAuditEntries
            .Where(entry => entry.ProviderOperationId == operation.Id).Select(entry => entry.Code).SingleAsync());
    }

    [Fact]
    public async Task RejectedRefund_LeavesSuccessfulPaymentScheduledForReconciliation()
    {
        this._provider.CreateRefundErrorCode = "refund_rejected";
        this._provider.CreateRefundIsDefinitive = true;
        var scheduledCheck = this._timeProvider.GetUtcNow().AddMinutes(20);
        var operation = this.CreateRefundOperation(1000);
        await this.SaveRefundAsync(operation, scheduledCheck);

        await using (var scope = this._host!.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PaymentProcessor>()
                .ProcessOperationAsync(operation.Id, CancellationToken.None);
        }

        var outcome = await this.ReadOperationEventAsync();
        await using var verificationScope = this._host!.Services.CreateAsyncScope();
        var database = verificationScope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var order = await database.PaymentOrders.AsNoTracking().SingleAsync(item => item.Id == this._paymentOrderId);
        var result = await database.ProviderOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id);
        Assert.Equal("Rejected", outcome.Status);
        Assert.Equal("refund_rejected", outcome.ErrorCode);
        Assert.Equal(OperationStatus.Rejected, result.Status);
        Assert.Equal(scheduledCheck.UtcTicks / 10, order.NextCheckAt.UtcTicks / 10);
        Assert.Equal("refund_rejected", await database.PaymentAuditEntries
            .Where(entry => entry.ProviderOperationId == operation.Id).Select(entry => entry.Code).SingleAsync());
    }

    [Fact]
    public async Task OperationFromDisabledMode_IsQuarantinedWithoutProviderRequest()
    {
        var operation = this.CreateRefundOperation(1000);
        operation.ExecutionMode = "Disabled";
        await using (var scope = this._host!.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            database.ProviderOperations.Add(operation);
            await database.SaveChangesAsync();
        }

        await using (var scope = this._host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PaymentProcessor>()
                .ProcessOperationAsync(operation.Id, CancellationToken.None);
        }

        await using var verificationScope = this._host!.Services.CreateAsyncScope();
        var result = await verificationScope.ServiceProvider.GetRequiredService<PaymentDbContext>()
            .ProviderOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id);
        Assert.Equal(OperationStatus.ManualReview, result.Status);
        Assert.Equal("payment_execution_mode_mismatch", result.LastError);
        Assert.Equal(0, this._provider.CreateRefundCalls);
    }

    private ProviderOperation CreateRefundOperation(long amountMinor) => new()
    {
        Id = Guid.NewGuid(),
        PaymentOrderId = this._paymentOrderId,
        CommandId = Guid.NewGuid(),
        Kind = OperationKind.Refund,
        ExecutionMode = "YooKassaTest",
        AmountMinor = amountMinor,
        RequestBody = "{}",
        IdempotenceKey = Guid.NewGuid().ToString("D"),
        Status = OperationStatus.Prepared,
        CreatedAt = this._timeProvider.GetUtcNow(),
        UpdatedAt = this._timeProvider.GetUtcNow(),
        PostBefore = this._timeProvider.GetUtcNow().AddHours(23),
        NextAttemptAt = this._timeProvider.GetUtcNow().AddMinutes(-1)
    };

    private async Task SaveRefundAsync(ProviderOperation operation, DateTimeOffset scheduledCheck)
    {
        await using var scope = this._host!.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var order = await database.PaymentOrders.SingleAsync(item => item.Id == this._paymentOrderId);
        order.Status = PaymentStatus.Succeeded;
        order.NextCheckAt = scheduledCheck;
        database.ProviderOperations.Add(operation);
        await database.SaveChangesAsync();
    }

    private async Task<PaymentOperationStateChanged> ReadOperationEventAsync()
    {
        var stopAt = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < stopAt)
        {
            if (this._operationEvents.TryDequeue(out var result))
            {
                return result;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Результат операции не поступил в очередь событий.");
    }

    private ProviderPayment CreateProviderPayment() => new(
        this._providerPaymentId,
        "succeeded",
        12500,
        "RUB",
        Paid: true,
        IsTest: true,
        RefundedAmountMinor: 0,
        ConfirmationUrl: null,
        PaymentOrderId: this._paymentOrderId.ToString(),
        MerchantAccountId: "test-shop");

    private async Task<PaymentStateChanged> ReadEventAsync()
    {
        var stopAt = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < stopAt)
        {
            if (this._events.TryDequeue(out var paymentState))
            {
                return paymentState;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Обновление оплаты не поступило в очередь событий.");
    }

    private sealed class RecordingPaymentProvider(Func<ProviderPayment> getPayment) : IPaymentProvider
    {
        private int _createRefundCalls;

        public string RefundStatus { get; set; } = "succeeded";
        public long RefundAmount { get; set; } = 12500;
        public string? CreateRefundErrorCode { get; set; }
        public bool CreateRefundIsDefinitive { get; set; }

        public int CreateRefundCalls => Volatile.Read(ref this._createRefundCalls);

        public string BuildPaymentRequest(PaymentOrder order) => "{}";

        public string BuildRefundRequest(PaymentOrder order, long amountMinor, string reason) => "{}";

        public Task<ProviderPayment> CreatePaymentAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken) =>
            Task.FromResult(getPayment());

        public Task<ProviderPayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken) =>
            Task.FromResult(getPayment());

        public Task<ProviderRefund> CreateRefundAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this._createRefundCalls);
            if (this.CreateRefundErrorCode is { } errorCode)
            {
                throw new PaymentProviderException(errorCode, this.CreateRefundIsDefinitive);
            }

            return Task.FromResult(new ProviderRefund("provider-refund-1", "provider-payment-1", this.RefundStatus,
                this.RefundAmount, "RUB"));
        }

        public Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderRefund("provider-refund-1", "provider-payment-1", "succeeded", 12500, "RUB"));
    }

    private sealed class PaymentOperationStateChangedConsumer(ConcurrentQueue<PaymentOperationStateChanged> events)
        : IConsumer<PaymentOperationStateChanged>
    {
        public Task Consume(ConsumeContext<PaymentOperationStateChanged> context)
        {
            events.Enqueue(context.Message);

            return Task.CompletedTask;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        private DateTimeOffset _currentTime = currentTime;
        private int _readCount;
        private int _advanceOnRead;
        private TimeSpan _advanceBy;

        public override DateTimeOffset GetUtcNow()
        {
            var readCount = Interlocked.Increment(ref this._readCount);
            if (readCount == Volatile.Read(ref this._advanceOnRead))
            {
                this._currentTime = this._currentTime.Add(this._advanceBy);
            }

            return this._currentTime;
        }

        public void AdvanceOnRead(int readNumber, TimeSpan amount)
        {
            this._readCount = 0;
            this._advanceBy = amount;
            this._advanceOnRead = readNumber;
        }
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
