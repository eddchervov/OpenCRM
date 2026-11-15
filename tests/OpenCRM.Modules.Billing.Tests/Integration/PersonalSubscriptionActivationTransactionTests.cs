using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using Npgsql;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class PersonalSubscriptionActivationTransactionTests(BillingDatabaseFixture fixture) : IAsyncLifetime
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
        using var services = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = services.CreateScope();
        var activation = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>();

        var result = await PersonalSubscriptionActivationTransactionTests.ActivateAsync(activation, payment, extend);

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
        using var firstServices = fixture.CreateServices(firstContext, fixture.CreateDefaultsInitializer().Object);
        using var secondServices = fixture.CreateServices(secondContext, fixture.CreateDefaultsInitializer().Object);
        using var firstScope = firstServices.CreateScope();
        using var secondScope = secondServices.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>();
        var second = secondScope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>();

        var results = await Task.WhenAll(
            PersonalSubscriptionActivationTransactionTests.ActivateAsync(first, payment, extend),
            PersonalSubscriptionActivationTransactionTests.ActivateAsync(second, payment, extend));

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
        using var services = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = services.CreateScope();
        var activation = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>();

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            PersonalSubscriptionActivationTransactionTests.ActivateAsync(activation, payment, extend));
        Assert.Empty(context.ChangeTracker.Entries());
        await using (var verification = fixture.CreateContext())
        {
            Assert.Equal("pending", (await verification.SubscriptionPayments.SingleAsync()).PaymentStatus);
            Assert.Empty(await verification.UserSubscriptionHistory.ToListAsync());
            Assert.Empty(await verification.TelegramNotificationOutbox.ToListAsync());
            if (extend)
            {
                Assert.Equal(payment.OldEndDate, (await verification.UserSubscriptions.SingleAsync()).EndDate);
            }
            else
            {
                Assert.Empty(await verification.UserSubscriptions.ToListAsync());
            }
        }

        Assert.True((await PersonalSubscriptionActivationTransactionTests.ActivateAsync(activation, payment, extend)).Success);
        await this.AssertSingleActivationAsync(payment, extend);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivationRefreshesPretrackedSubscriptionWithoutDiscardingUnrelatedTrackedUser(bool extend)
    {
        await using var seed = fixture.CreateContext();
        var payment = await this.CreatePaymentAsync(seed, true);
        if (!extend)
        {
            var previousPlan = await fixture.CreatePlanAsync(seed, "Прежний личный");
            previousPlan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
            previousPlan.Order = 1;
            await seed.SaveChangesAsync();
            await seed.UserSubscriptions.ExecuteUpdateAsync(update =>
                update.SetProperty(subscription => subscription.SubscriptionPlanId, previousPlan.Id));
            await seed.SubscriptionPayments.ExecuteUpdateAsync(update =>
                update.SetProperty(item => item.PaymentMethod, "yookassa_purchase"));
        }

        await using var context = fixture.CreateContext();
        var previous = await context.UserSubscriptions.SingleAsync();
        var trackedUser = await context.Users.SingleAsync();
        using var services = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = services.CreateScope();
        var activation = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>();

        Assert.True((await PersonalSubscriptionActivationTransactionTests.ActivateAsync(activation, payment, extend)).Success);
        Assert.DoesNotContain(context.ChangeTracker.Entries<UserSubscription>(), entry => entry.Entity.Id == previous.Id);
        Assert.Equal(EntityState.Unchanged, context.Entry(trackedUser).State);
        await context.SaveChangesAsync();
        await this.AssertSingleActivationAsync(payment, extend, extend ? "extended" : "replaced");
    }

    private async Task<PaymentCase> CreatePaymentAsync(OpenCRMContext context, bool extend)
    {
        var user = new ApplicationUser { Email = "activation-retry@example.test" };
        context.Users.Add(user);
        var plan = await fixture.CreatePlanAsync(context, "Платный для повторов");
        plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        var now = DateTime.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));
        var endDate = now.AddDays(3);
        if (extend)
        {
            context.UserSubscriptions.Add(new UserSubscription
            {
                UserId = user.Id, SubscriptionPlanId = plan.Id,
                StartDate = now.AddDays(-27), EndDate = endDate, SourceCode = "paid"
            });
        }

        var externalId = Guid.NewGuid().ToString("N");
        context.SubscriptionPayments.Add(new SubscriptionPayment
        {
            UserId = user.Id, SubscriptionPlanId = plan.Id, Amount = plan.Price,
            ExternalPaymentId = externalId, PaymentStatus = "pending", PaidAt = now,
            PaymentMethod = extend ? "yookassa_extension" : "yookassa_purchase", CreatedAt = now
        });
        await context.SaveChangesAsync();

        return new PaymentCase(user.Id, plan.Id, externalId, endDate, plan.DurationDays);
    }

    private async Task AssertSingleActivationAsync(PaymentCase payment, bool extend, string? expectedStatus = null)
    {
        await using var verification = fixture.CreateContext();
        var current = await verification.UserSubscriptions.SingleAsync();
        Assert.Equal(payment.UserId, current.UserId);
        Assert.Equal(payment.PlanId, current.SubscriptionPlanId);
        Assert.Equal("paid", current.SourceCode);
        if (extend)
        {
            Assert.Equal(payment.OldEndDate.AddDays(payment.DurationDays), current.EndDate);
        }

        var history = await verification.UserSubscriptionHistory.SingleAsync();
        Assert.Equal(expectedStatus ?? (extend ? "extended" : "created"), history.Status);
        Assert.Equal(current.EndDate, history.EndDate);
        Assert.Equal("completed", (await verification.SubscriptionPayments.SingleAsync()).PaymentStatus);
        Assert.Equal("subscription-paid:" + payment.ExternalId,
            (await verification.TelegramNotificationOutbox.SingleAsync()).NotificationKey);
    }

    private static Task<OpenCRM.Services.Interfaces.Subscriptions.Dtos.SubscriptionResult> ActivateAsync(
        IPersonalSubscriptionActivationService activation,
        PaymentCase payment,
        bool extend) => extend
            ? activation.ActivateExtendedSubscriptionAsync(payment.UserId, payment.PlanId, payment.ExternalId)
            : activation.ActivateNewSubscriptionAsync(payment.UserId, payment.PlanId, payment.ExternalId);

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record PaymentCase(string UserId, int PlanId, string ExternalId, DateTime OldEndDate, int DurationDays);

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
