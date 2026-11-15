using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using OpenCRM.Infrastructure.Payments;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Testing;
using Npgsql;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class PaymentCreationOperationStoreTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Fact]
    public async Task CompatibleLateSuccessFinalizesOnceAndStaleErrorsCannotChangeNewLeaseOrResult()
    {
        var operation = await this.NewOperationAsync();
        await using var firstContext = fixture.CreateContext();
        var firstStore = new PaymentCreationOperationStore(firstContext);
        await firstStore.PrepareAsync(operation, true);
        var firstLease = Assert.IsType<PaymentCreationLease>(await firstStore.AcquireLeaseAsync(operation.Id));
        await using var secondContext = fixture.CreateContext();
        await secondContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "PaymentCreationOperations" SET "LeaseUntil" = {DateTime.UtcNow.AddMinutes(-1)} WHERE "Id" = {operation.Id}
            """);
        var secondStore = new PaymentCreationOperationStore(secondContext);
        var secondLease = Assert.IsType<PaymentCreationLease>(await secondStore.AcquireLeaseAsync(operation.Id));
        Assert.NotEqual(firstLease.LeaseToken, secondLease.LeaseToken);

        await firstStore.RecordFailureAsync(firstLease, PaymentCreationOperationStatuses.Rejected, "stale_error");
        await using var verification = fixture.CreateContext();
        var leased = await verification.PaymentCreationOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id);
        Assert.Equal(secondLease.LeaseToken, leased.LeaseToken);
        Assert.Equal(PaymentCreationOperationStatuses.Sending, leased.Status);

        var response = PaymentCreationOperationStoreTests.ProviderResponse(operation);
        Assert.True((await firstStore.FinalizeSuccessAsync(firstLease, response))!.IsSuccess);
        await secondStore.RecordFailureAsync(secondLease, PaymentCreationOperationStatuses.Rejected, "late_error");
        Assert.True((await secondStore.FinalizeSuccessAsync(secondLease, response))!.IsSuccess);
        var completed = await verification.PaymentCreationOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id);
        Assert.Equal(PaymentCreationOperationStatuses.Created, completed.Status);
        Assert.Null(completed.LastErrorCode);
        Assert.Equal(1, await verification.SubscriptionPayments.CountAsync(payment => payment.ExternalPaymentId == response.Id));
    }

    [Theory]
    [InlineData(PaymentCreationOperationStatuses.Rejected)]
    [InlineData(PaymentCreationOperationStatuses.ManualReview)]
    public async Task LateSuccessCannotReopenTerminalOperation(string status)
    {
        var operation = await this.NewOperationAsync();
        await using var context = fixture.CreateContext();
        var store = new PaymentCreationOperationStore(context);
        await store.PrepareAsync(operation, true);
        var lease = Assert.IsType<PaymentCreationLease>(await store.AcquireLeaseAsync(operation.Id));
        await store.RecordFailureAsync(lease, status, "terminal_error");

        Assert.False((await store.FinalizeSuccessAsync(lease, PaymentCreationOperationStoreTests.ProviderResponse(operation)))!.IsSuccess);
        Assert.Equal(status, (await context.PaymentCreationOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id)).Status);
        Assert.False(await context.SubscriptionPayments.AnyAsync(payment => payment.ExternalPaymentId == operation.IdempotenceKey));
    }

    [Fact]
    public async Task IncompatibleLeaseCannotCreatePayment()
    {
        var operation = await this.NewOperationAsync();
        await using var context = fixture.CreateContext();
        var store = new PaymentCreationOperationStore(context);
        await store.PrepareAsync(operation, true);
        var lease = Assert.IsType<PaymentCreationLease>(await store.AcquireLeaseAsync(operation.Id));

        Assert.False((await store.FinalizeSuccessAsync(lease with { RequestBody = "changed" },
            PaymentCreationOperationStoreTests.ProviderResponse(operation)))!.IsSuccess);
        Assert.False(await context.SubscriptionPayments.AnyAsync(payment => payment.ExternalPaymentId == operation.IdempotenceKey));
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("lease")]
    [InlineData("failure")]
    [InlineData("finalize")]
    public async Task DirtyTargetIsRejectedWithoutLosingUnsavedChanges(string action)
    {
        var operation = await this.NewOperationAsync();
        await using var context = fixture.CreateContext();
        var store = new PaymentCreationOperationStore(context);
        await store.PrepareAsync(operation, true);
        var lease = Assert.IsType<PaymentCreationLease>(await store.AcquireLeaseAsync(operation.Id));
        var tracked = context.PaymentCreationOperations.Local.Single();
        tracked.Description = "Несохранённое изменение";

        await Assert.ThrowsAsync<InvalidOperationException>(() => action switch
        {
            "prepare" => store.PrepareAsync(operation, true),
            "lease" => store.AcquireLeaseAsync(operation.Id),
            "failure" => store.RecordFailureAsync(lease, PaymentCreationOperationStatuses.Prepared, "error"),
            _ => store.FinalizeSuccessAsync(lease, PaymentCreationOperationStoreTests.ProviderResponse(operation))
        });
        Assert.Same(tracked, context.PaymentCreationOperations.Local.Single());
        Assert.Equal("Несохранённое изменение", tracked.Description);
        Assert.Equal(EntityState.Modified, context.Entry(tracked).State);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(22, true)]
    [InlineData(23, false)]
    [InlineData(24, false)]
    [InlineData(25, false)]
    public async Task ProviderWindowStartsAtFirstAttemptInsteadOfPreparation(int? firstAttemptAgeHours, bool canSend)
    {
        var operation = await this.NewOperationAsync();
        operation.CreatedAt = DateTime.UtcNow.AddDays(-3);
        operation.FirstAttemptAt = firstAttemptAgeHours.HasValue ? DateTime.UtcNow.AddHours(-firstAttemptAgeHours.Value) : null;
        await using var context = fixture.CreateContext();
        var store = new PaymentCreationOperationStore(context);
        await store.PrepareAsync(operation, true);

        Assert.Equal(canSend, await store.AcquireLeaseAsync(operation.Id) != null);
        var saved = await context.PaymentCreationOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id);
        Assert.Equal(canSend ? PaymentCreationOperationStatuses.Sending : PaymentCreationOperationStatuses.ManualReview, saved.Status);
        Assert.Equal(canSend ? null : "operation_expired", saved.LastErrorCode);
    }

    [Fact]
    public async Task AmbientTransactionIsRejectedBeforeChangingOperation()
    {
        var operation = await this.NewOperationAsync();
        await using var context = fixture.CreateContext();
        var store = new PaymentCreationOperationStore(context);
        await store.PrepareAsync(operation, true);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AcquireLeaseAsync(operation.Id));
        });
        Assert.Equal(PaymentCreationOperationStatuses.Prepared,
            (await context.PaymentCreationOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareRecoversCommitFailureWithoutDuplicatingPersistedOperation(bool afterCommit)
    {
        var operation = await this.NewOperationAsync();
        var interceptor = new CommitFailureInterceptor(afterCommit);
        await using var context = this.CreateRetryContext(interceptor);

        var prepared = await new PaymentCreationOperationStore(context).PrepareAsync(operation, true);

        Assert.Equal(operation.Id, prepared.Id);
        Assert.Equal(1, interceptor.FailureCount);
        await using var verification = fixture.CreateContext();
        Assert.Equal(1, await verification.PaymentCreationOperations.CountAsync(item => item.IdempotenceKey == operation.IdempotenceKey));
    }

    [Fact]
    public async Task AmbientTransactionScopeCannotPrepareAnExternalPayment()
    {
        var operation = await this.NewOperationAsync();
        await using var context = fixture.CreateContext();
        using (var transaction = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PaymentCreationOperationStore(context).PrepareAsync(operation, true));
        }

        await using var verification = fixture.CreateContext();
        Assert.False(await verification.PaymentCreationOperations.AnyAsync(item => item.IdempotenceKey == operation.IdempotenceKey));
    }

    [Fact]
    public async Task ConcurrentPreparationReturnsTheSameCommittedOperation()
    {
        var operation = await this.NewOperationAsync();
        var copy = await this.NewOperationAsync();
        copy.IdempotenceKey = operation.IdempotenceKey;
        copy.UserId = operation.UserId;
        copy.SubscriptionPlanId = operation.SubscriptionPlanId;
        copy.RequestTimestamp = operation.RequestTimestamp;
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();

        var results = await Task.WhenAll(
            new PaymentCreationOperationStore(firstContext).PrepareAsync(operation, true),
            new PaymentCreationOperationStore(secondContext).PrepareAsync(copy, true));

        Assert.Equal(results[0].Id, results[1].Id);
        await using var verification = fixture.CreateContext();
        Assert.Equal(1, await verification.PaymentCreationOperations.CountAsync(item => item.IdempotenceKey == operation.IdempotenceKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalizationRecoversCommitFailureWithoutDuplicatingPayment(bool afterCommit)
    {
        var operation = await this.NewOperationAsync();
        await using var preparation = fixture.CreateContext();
        var store = new PaymentCreationOperationStore(preparation);
        await store.PrepareAsync(operation, true);
        var lease = Assert.IsType<PaymentCreationLease>(await store.AcquireLeaseAsync(operation.Id));
        var interceptor = new CommitFailureInterceptor(afterCommit);
        await using var context = this.CreateRetryContext(interceptor);

        var result = await new PaymentCreationOperationStore(context).FinalizeSuccessAsync(
            lease, PaymentCreationOperationStoreTests.ProviderResponse(operation));

        Assert.True(result!.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, interceptor.FailureCount);
        await using var verification = fixture.CreateContext();
        Assert.Equal(1, await verification.SubscriptionPayments.CountAsync(payment => payment.ExternalPaymentId == operation.IdempotenceKey));
        Assert.Equal(PaymentCreationOperationStatuses.Created,
            (await verification.PaymentCreationOperations.SingleAsync(item => item.Id == operation.Id)).Status);
    }

    private async Task<PaymentCreationOperation> NewOperationAsync()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var plan = new SubscriptionPlan { Name = "Тест операции", Price = 500, DurationDays = 30 };
        context.Users.Add(user);
        context.SubscriptionPlans.Add(plan);
        await context.SaveChangesAsync();
        var now = DateTime.UtcNow;

        return new PaymentCreationOperation
        {
            IdempotenceKey = Guid.NewGuid().ToString("N"), UserId = user.Id, SubscriptionPlanId = plan.Id,
            OperationType = "yookassa_purchase", Amount = 500, Description = "Снимок оплаты", BuyerTypeCode = "individual",
            RequestTimestamp = now.AddTicks(-(now.Ticks % 10)), RequestBody = "{}", CreatedAt = now
        };
    }

    [Fact]
    public async Task RegisteredAutomationRecoversPreparedOperationsThroughItsOwnGateway()
    {
        var operation = await this.NewOperationAsync();
        await using var context = fixture.CreateContext();
        await new PaymentCreationOperationStore(context).PrepareAsync(operation, true);
        var handler = new RecoveryHandler();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(context);
        services.AddSingleton(Mock.Of<IPromotionPaymentCoordinator>());
        services.AddOpenCRMPaymentsInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["YooKassa:ShopId"] = "test-shop", ["YooKassa:SecretKey"] = "test-secret"
        }).Build());
        services.AddHttpClient("OpenCRM.PaymentCreation").ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton(Mock.Of<IPaymentConfirmationService>());
        var providerGateway = new Mock<IYooKassaService>();
        providerGateway.Setup(service => service.GetPaymentStatusAsync(It.IsAny<string>()))
            .ReturnsAsync(PaymentResponse.CreateError("Тестовый провайдер недоступен."));
        services.AddSingleton(providerGateway.Object);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IBillingAutomationService>().ReconcilePaymentsAsync();

        Assert.Contains(operation.IdempotenceKey, handler.Keys);
        var saved = await context.PaymentCreationOperations.AsNoTracking().SingleAsync(item => item.Id == operation.Id);
        Assert.Equal(PaymentCreationOperationStatuses.Prepared, saved.Status);
        Assert.Equal(1, saved.AttemptCount);
    }

    private OpenCRMContext CreateRetryContext(IInterceptor interceptor)
    {
        using var template = fixture.CreateContext();

        return new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(template.Database.GetConnectionString(), database =>
            {
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
                database.EnableRetryOnFailure();
            }).AddInterceptors(interceptor).Options);
    }

    [Theory]
    [InlineData(503, "[]")]
    [InlineData(503, "null")]
    [InlineData(503, "\"не объект\"")]
    [InlineData(503, "{\"code\":32}")]
    [InlineData(429, "{\"code\":\"too_many_requests\"}")]
    [InlineData(200, "[]")]
    [InlineData(200, "{\"id\":\"incomplete\"}")]
    public async Task UnknownOrIncompleteProviderResponseRemainsRecoverable(int statusCode, string body)
    {
        var operation = await this.NewOperationAsync();
        await using var context = fixture.CreateContext();
        using var client = new HttpClient(new StaticResponseHandler(statusCode, body))
        {
            BaseAddress = new Uri("https://example.test/")
        };

        var processor = new PaymentCreationProcessor(client, context, NullLogger.Instance,
            new YooKassaConfig { ShopId = "test", SecretKey = "test" });

        var result = await processor.CreatePaymentAsync(operation.UserId, operation.SubscriptionPlanId, operation.Amount,
            operation.Description, operation.OperationType, new PaymentBuyerDto { TypeCode = "individual" },
            "https://example.test/complete", null, operation.IdempotenceKey, operation.RequestTimestamp);

        Assert.False(result.IsSuccess);
        var saved = await context.PaymentCreationOperations.AsNoTracking().SingleAsync(item => item.IdempotenceKey == operation.IdempotenceKey);
        Assert.Equal(PaymentCreationOperationStatuses.Prepared, saved.Status);
        Assert.Null(saved.LeaseToken);
        Assert.False(await context.SubscriptionPayments.AnyAsync(payment => payment.UserId == operation.UserId));
    }

    private static YooKassaPaymentResponse ProviderResponse(PaymentCreationOperation operation) => new()
    {
        Id = operation.IdempotenceKey, CreatedAt = DateTime.UtcNow,
        Confirmation = new YooKassaConfirmation { Type = "redirect", ConfirmationUrl = "https://example.test/pay" }
    };

    private sealed class CommitFailureInterceptor(bool afterCommit) : DbTransactionInterceptor
    {
        public int FailureCount { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!afterCommit)
            {
                this.FailOnce();
            }

            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (afterCommit)
            {
                this.FailOnce();
            }

            return Task.CompletedTask;
        }

        private void FailOnce()
        {
            if (this.FailureCount != 0)
            {
                return;
            }

            this.FailureCount++;

            throw new PostgresException("Тестовый сбой фиксации операции оплаты.", "ERROR", "ERROR", "40001");
        }
    }

    private sealed class RecoveryHandler : HttpMessageHandler
    {
        public List<string> Keys { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Keys.Add(request.Headers.GetValues("Idempotence-Key").Single());

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        }
    }

    private sealed class StaticResponseHandler(int statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)statusCode)
            {
                Content = new StringContent(body)
            });
    }
}
