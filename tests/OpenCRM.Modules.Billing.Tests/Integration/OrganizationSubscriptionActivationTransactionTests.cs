using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Modules.Organizations;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using Npgsql;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class OrganizationSubscriptionActivationTransactionTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TransientFailureBeforeOrAfterCommitDoesNotIssueSubscriptionTwice(bool extend, bool afterCommit)
    {
        await using var seed = fixture.CreateContext();
        var payment = await this.CreatePaymentAsync(seed, extend);
        var failure = new CommitFailureInterceptor(afterCommit);
        await using var context = fixture.CreateContext(failure);
        using var services = this.CreateServices(context);
        using var scope = services.CreateScope();
        var activation = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>();

        var result = await OrganizationSubscriptionActivationTransactionTests.ActivateAsync(activation, payment, extend);

        Assert.True(result.Success);
        Assert.Equal(1, failure.FailureCount);
        Assert.Equal(2, failure.CommitAttempts);
        await this.AssertSingleActivationAsync(payment, extend);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentDuplicateConfirmationsActivateOneSubscription(bool extend)
    {
        await using var seed = fixture.CreateContext();
        var payment = await this.CreatePaymentAsync(seed, extend);
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        using var firstServices = this.CreateServices(firstContext);
        using var secondServices = this.CreateServices(secondContext);
        using var firstScope = firstServices.CreateScope();
        using var secondScope = secondServices.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>();
        var second = secondScope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>();

        var results = await Task.WhenAll(
            OrganizationSubscriptionActivationTransactionTests.ActivateAsync(first, payment, extend),
            OrganizationSubscriptionActivationTransactionTests.ActivateAsync(second, payment, extend));

        Assert.All(results, result => Assert.True(result.Success));
        await this.AssertSingleActivationAsync(payment, extend);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PermanentSaveFailureRollsBackAllWritesAndSameContextCanRetry(bool extend)
    {
        await using var seed = fixture.CreateContext();
        var payment = await this.CreatePaymentAsync(seed, extend);
        var failure = new SaveFailureInterceptor();
        await using var context = fixture.CreateContext(failure);
        using var services = this.CreateServices(context);
        using var scope = services.CreateScope();
        var activation = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>();

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            OrganizationSubscriptionActivationTransactionTests.ActivateAsync(activation, payment, extend));
        Assert.Empty(context.ChangeTracker.Entries());
        await using (var verification = fixture.CreateContext())
        {
            Assert.Equal("pending", (await verification.SubscriptionPayments.SingleAsync()).PaymentStatus);
            Assert.Empty(await verification.OrganizationAuditEvents.ToListAsync());
            Assert.Empty(await verification.TelegramNotificationOutbox.ToListAsync());
            if (extend)
            {
                Assert.Equal(payment.OldEndDate, (await verification.OrganizationSubscriptions.SingleAsync()).EndDate);
            }
            else
            {
                Assert.Empty(await verification.OrganizationSubscriptions.ToListAsync());
            }
        }

        Assert.True((await OrganizationSubscriptionActivationTransactionTests.ActivateAsync(activation, payment, extend)).Success);
        await this.AssertSingleActivationAsync(payment, extend);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentDistinctPaidOperationsForSameOrganizationCannotBypassPurchaseOrExtensionPolicy(bool extend)
    {
        await using var seed = fixture.CreateContext();
        var firstPayment = await this.CreatePaymentAsync(seed, extend);
        var secondPayment = firstPayment with { ExternalId = Guid.NewGuid().ToString("N") };
        var original = await seed.SubscriptionPayments.AsNoTracking().SingleAsync();
        seed.SubscriptionPayments.Add(new SubscriptionPayment
        {
            UserId = original.UserId, SubscriptionPlanId = original.SubscriptionPlanId,
            ExternalPaymentId = secondPayment.ExternalId, PaymentMethod = original.PaymentMethod,
            PaymentStatus = "pending", PaidAt = original.PaidAt, Amount = original.Amount, CreatedAt = original.CreatedAt
        });
        await seed.SaveChangesAsync();
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        using var firstServices = this.CreateServices(firstContext);
        using var secondServices = this.CreateServices(secondContext);
        using var firstScope = firstServices.CreateScope();
        using var secondScope = secondServices.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>();
        var second = secondScope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>();

        var results = await Task.WhenAll(
            OrganizationSubscriptionActivationTransactionTests.ActivateAsync(first, firstPayment, extend),
            OrganizationSubscriptionActivationTransactionTests.ActivateAsync(second, secondPayment, extend));

        Assert.Single(results, result => result.Success);
        Assert.Single(results, result => !result.Success);
        await using var verification = fixture.CreateContext();
        var subscription = await verification.OrganizationSubscriptions.SingleAsync();
        if (extend)
        {
            Assert.Equal(firstPayment.OldEndDate.AddDays(firstPayment.DurationDays), subscription.EndDate);
        }

        var payments = await verification.SubscriptionPayments.AsNoTracking().ToListAsync();
        Assert.Single(payments, payment => payment.PaymentStatus == "completed");
        Assert.Single(payments, payment => payment.PaymentStatus == "failed");
        Assert.All(payments, payment => Assert.NotNull(payment.PaidAt));
        Assert.Single(await verification.OrganizationAuditEvents.ToListAsync());
        Assert.Single(await verification.TelegramNotificationOutbox.ToListAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ActivationRefreshesPretrackedSubscriptionWithoutDiscardingUnrelatedTrackedUser(
        bool extend,
        bool expiredSnapshot)
    {
        await using var seed = fixture.CreateContext();
        var payment = await this.CreatePaymentAsync(seed, true);
        if (!extend)
        {
            var previousPlan = await fixture.CreatePlanAsync(seed, "Прежний базовый", isDefault: true);
            await seed.OrganizationSubscriptions.ExecuteUpdateAsync(update =>
                update.SetProperty(subscription => subscription.SubscriptionPlanId, previousPlan.Id));
            await seed.SubscriptionPayments.ExecuteUpdateAsync(update =>
                update.SetProperty(item => item.PaymentMethod, $"yookassa_workspace_purchase:{payment.OrganizationId:D}"));
        }

        if (expiredSnapshot)
        {
            await seed.OrganizationSubscriptions.ExecuteUpdateAsync(update =>
                update.SetProperty(subscription => subscription.EndDate, DateTime.UtcNow.AddDays(-1)));
        }

        await using var context = fixture.CreateContext();
        var previous = await context.OrganizationSubscriptions.SingleAsync();
        var trackedUser = await context.ApplicationUsers.SingleAsync();
        if (expiredSnapshot)
        {
            await seed.OrganizationSubscriptions.ExecuteUpdateAsync(update =>
                update.SetProperty(subscription => subscription.EndDate, payment.OldEndDate));
        }

        using var services = this.CreateServices(context);
        using var scope = services.CreateScope();
        var activation = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>();

        Assert.True((await OrganizationSubscriptionActivationTransactionTests.ActivateAsync(activation, payment, extend)).Success);
        Assert.DoesNotContain(context.ChangeTracker.Entries<OrganizationSubscription>(), entry => entry.Entity.Id == previous.Id);
        Assert.Equal(EntityState.Unchanged, context.Entry(trackedUser).State);
        var refreshed = await context.OrganizationSubscriptions.FindAsync(previous.Id);
        Assert.NotNull(refreshed);
        if (extend)
        {
            Assert.Equal(payment.OldEndDate.AddDays(payment.DurationDays), refreshed.EndDate);
        }
        else
        {
            Assert.True(refreshed.EndDate < payment.OldEndDate);
        }

        trackedUser.Email = "organization-activation-after@example.test";
        await context.SaveChangesAsync();
        await using var verification = fixture.CreateContext();
        Assert.Equal(refreshed.EndDate, (await verification.OrganizationSubscriptions.FindAsync(previous.Id))!.EndDate);
        Assert.Equal(trackedUser.Email, (await verification.ApplicationUsers.SingleAsync()).Email);
        Assert.Single(await verification.OrganizationAuditEvents.ToListAsync());
        Assert.Single(await verification.TelegramNotificationOutbox.ToListAsync());
    }

    private ServiceProvider CreateServices(OpenCRMContext context)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(context);
        services.AddSingleton(fixture.CreateDefaultsInitializer().Object);
        services.AddSingleton(fixture.CreatePromoCodeService().Object);
        services.AddScoped<IOrganizationAuditService, OrganizationAuditService>();
        services.AddScoped<IOrganizationPermissionService, OrganizationPermissionService>();
        services.AddSingleton(Mock.Of<IPaymentCreationGateway>());
        services.AddSingleton(Moq.Mock.Of<OpenCRM.Services.Interfaces.Payments.IYooKassaService>());
        services.AddSingleton(Mock.Of<IWorkspaceAccessService>());
        services.AddBillingExternalDependencies();
        services.AddOpenCRMBilling();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private async Task<PaymentCase> CreatePaymentAsync(OpenCRMContext context, bool extend)
    {
        var organization = await fixture.CreateOrganizationAsync(context);
        var userId = organization.OwnerUserId;
        var plan = await fixture.CreatePlanAsync(context, "Платный для повторов");
        var now = DateTime.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        var endDate = now.AddDays(3);
        if (extend)
        {
            context.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                OrganizationId = organization.Id, SubscriptionPlanId = plan.Id,
                StartDate = now.AddDays(-27), EndDate = endDate, SourceCode = "paid"
            });
        }

        var externalId = Guid.NewGuid().ToString("N");
        context.SubscriptionPayments.Add(new SubscriptionPayment
        {
            UserId = userId, SubscriptionPlanId = plan.Id, Amount = plan.Price,
            ExternalPaymentId = externalId, PaymentStatus = "pending", PaidAt = now,
            PaymentMethod = $"yookassa_workspace_{(extend ? "extension" : "purchase")}:{organization.Id:D}", CreatedAt = now
        });
        await context.SaveChangesAsync();

        return new PaymentCase(userId, organization.Id, plan.Id, externalId, endDate, plan.DurationDays);
    }

    private async Task AssertSingleActivationAsync(PaymentCase payment, bool extend)
    {
        await using var verification = fixture.CreateContext();
        var current = await verification.OrganizationSubscriptions.SingleAsync();
        Assert.Equal(payment.OrganizationId, current.OrganizationId);
        Assert.Equal(payment.PlanId, current.SubscriptionPlanId);
        Assert.Equal("paid", current.SourceCode);
        if (extend)
        {
            Assert.Equal(payment.OldEndDate.AddDays(payment.DurationDays), current.EndDate);
        }

        Assert.Empty(await verification.UserSubscriptionHistory.ToListAsync());
        var audit = await verification.OrganizationAuditEvents.SingleAsync();
        Assert.Equal(extend ? "billing.subscription.extended" : "billing.subscription.activated", audit.EventType);
        Assert.Equal(payment.OrganizationId, audit.OrganizationId);
        Assert.Equal("completed", (await verification.SubscriptionPayments.SingleAsync()).PaymentStatus);
        Assert.Equal("subscription-paid:" + payment.ExternalId,
            (await verification.TelegramNotificationOutbox.SingleAsync()).NotificationKey);
    }

    private static Task<OpenCRM.Services.Interfaces.Subscriptions.Dtos.SubscriptionResult> ActivateAsync(
        IOrganizationSubscriptionActivationService activation,
        PaymentCase payment,
        bool extend) => extend
            ? activation.ActivateExtendedSubscriptionAsync(payment.UserId, payment.OrganizationId, payment.PlanId, payment.ExternalId)
            : activation.ActivateNewSubscriptionAsync(payment.UserId, payment.OrganizationId, payment.PlanId, payment.ExternalId);

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record PaymentCase(string UserId, Guid OrganizationId, int PlanId, string ExternalId, DateTime OldEndDate, int DurationDays);

    private sealed class CommitFailureInterceptor(bool afterCommit) : DbTransactionInterceptor
    {
        internal int CommitAttempts { get; private set; }
        internal int FailureCount { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            this.CommitAttempts++;
            if (!afterCommit)
            {
                this.FailOnce();
            }

            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (afterCommit)
            {
                this.FailOnce();
            }

            return Task.CompletedTask;
        }

        private void FailOnce()
        {
            if (this.FailureCount > 0)
            {
                return;
            }

            this.FailureCount++;

            throw new PostgresException("Временный сбой фиксации активации подписки.", "ERROR", "ERROR", "40001");
        }
    }

    private sealed class SaveFailureInterceptor : SaveChangesInterceptor
    {
        private bool _hasFailed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (this._hasFailed)
            {
                return ValueTask.FromResult(result);
            }

            this._hasFailed = true;

            throw new DbUpdateException("Тестовый постоянный отказ сохранения активации.");
        }
    }
}
