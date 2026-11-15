using Moq;
using OpenCRM.Modules.Billing.Contracts;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing;
using Npgsql;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class PersonalSubscriptionCommandTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    [Fact]
    public async Task TestingActivationPreservesValidationAndReplacesCurrentSubscriptionWithHistory()
    {
        await using var seed = fixture.CreateContext();
        var user = await this.CreateUserAsync(seed, canUseTestSubscriptions: false);
        var plan = await this.CreatePersonalPlanAsync(seed, "Тестовый активный");
        var inactivePlan = await this.CreatePersonalPlanAsync(seed, "Тестовый неактивный", isActive: false);
        await using var context = fixture.CreateContext();
        var commands = this.CreateService(context);

        Assert.Equal("Пользователь не найден", (await commands.ActivateSubscriptionForTestingAsync("missing", plan.Id)).Error);
        Assert.Equal("Тестовое управление тарифами недоступно.", (await commands.ActivateSubscriptionForTestingAsync(user.Id, plan.Id)).Error);
        var trackedUser = await context.Users.FindAsync(user.Id);
        trackedUser!.CanUseTestSubscriptions = true;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal("Тарифный план не найден или неактивен", (await commands.ActivateSubscriptionForTestingAsync(user.Id, int.MaxValue)).Error);
        Assert.Equal("Тарифный план не найден или неактивен", (await commands.ActivateSubscriptionForTestingAsync(user.Id, inactivePlan.Id)).Error);

        var previousPlan = await this.CreatePersonalPlanAsync(context, "Предыдущий тариф");
        var previous = new UserSubscription
        {
            UserId = user.Id,
            SubscriptionPlanId = previousPlan.Id,
            StartDate = DateTime.UtcNow.AddDays(-10),
            EndDate = DateTime.UtcNow.AddDays(10),
            SourceCode = "paid"
        };
        context.UserSubscriptions.Add(previous);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Assert.True((await commands.ActivateSubscriptionForTestingAsync(user.Id, plan.Id)).Success);
        await using var verification = fixture.CreateContext();
        var current = await verification.UserSubscriptions.SingleAsync();
        var history = await verification.UserSubscriptionHistory.SingleAsync(item => item.UserId == user.Id);
        Assert.Equal(plan.Id, current.SubscriptionPlanId);
        Assert.Equal("manual", current.SourceCode);
        Assert.Equal("replaced", history.Status);
        Assert.Equal($"Тестовая активация тарифа без оплаты. План: {plan.Name}", history.Comment);
    }

    [Fact]
    public async Task TestingExtensionAndCancellationPreserveValidationAndHistory()
    {
        await using var seed = fixture.CreateContext();
        var user = await this.CreateUserAsync(seed, canUseTestSubscriptions: false);
        var plan = await this.CreatePersonalPlanAsync(seed, "Продляемый тариф");
        await using var context = fixture.CreateContext();
        var commands = this.CreateService(context);

        Assert.Equal("Пользователь не найден", (await commands.ExtendSubscriptionForTestingAsync("missing")).Error);
        Assert.Equal("Тестовое управление тарифами недоступно.", (await commands.ExtendSubscriptionForTestingAsync(user.Id)).Error);
        Assert.Equal("Пользователь не найден", (await commands.CancelSubscriptionForTestingAsync("missing")).Error);
        Assert.Equal("Тестовое управление тарифами недоступно.", (await commands.CancelSubscriptionForTestingAsync(user.Id)).Error);
        var trackedUser = await context.Users.FindAsync(user.Id);
        trackedUser!.CanUseTestSubscriptions = true;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal("У вас нет активной подписки для продления", (await commands.ExtendSubscriptionForTestingAsync(user.Id)).Error);
        Assert.Equal("У вас нет активной подписки для отмены", (await commands.CancelSubscriptionForTestingAsync(user.Id)).Error);
        var oldEndDate = DateTime.UtcNow.AddDays(4);
        oldEndDate = oldEndDate.AddTicks(-(oldEndDate.Ticks % 10));
        context.UserSubscriptions.Add(new UserSubscription
        {
            UserId = user.Id,
            SubscriptionPlanId = plan.Id,
            StartDate = oldEndDate.AddDays(-20),
            EndDate = oldEndDate,
            SourceCode = "manual"
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Assert.True((await commands.ExtendSubscriptionForTestingAsync(user.Id)).Success);
        await using (var verification = fixture.CreateContext())
        {
            Assert.Equal(oldEndDate.AddDays(plan.DurationDays), (await verification.UserSubscriptions.SingleAsync()).EndDate);
            var history = await verification.UserSubscriptionHistory.SingleAsync(item => item.UserId == user.Id);
            Assert.Equal("extended", history.Status);
            Assert.Contains(oldEndDate.ToString("dd.MM.yyyy"), history.Comment);
        }

        Assert.True((await commands.CancelSubscriptionForTestingAsync(user.Id)).Success);
        await using var afterCancel = fixture.CreateContext();
        Assert.Empty(await afterCancel.UserSubscriptions.ToListAsync());
        var cancellation = await afterCancel.UserSubscriptionHistory.SingleAsync(history => history.UserId == user.Id && history.Status == "cancelled");
        Assert.NotNull(cancellation.ActualEndDate);
    }

    [Fact]
    public async Task DemoGrantPreservesEligibilityRulesAndCreatesSnapshot()
    {
        await using var seed = fixture.CreateContext();
        var user = await this.CreateUserAsync(seed);
        await seed.SubscriptionPlans
            .Where(plan => plan.IsDemoPlan && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal)
            .ExecuteUpdateAsync(update => update.SetProperty(plan => plan.IsActive, false));
        var demoPlan = await this.CreatePersonalPlanAsync(seed, "Демо", isDemo: true, isActive: false);
        var paidPlan = await this.CreatePersonalPlanAsync(seed, "Платный");
        await using var context = fixture.CreateContext();
        var commands = this.CreateService(context);

        Assert.Equal("Демо-тариф не настроен в системе", (await commands.GrantDemoSubscriptionAsync(user.Id)).Error);
        demoPlan.IsActive = true;
        await seed.SaveChangesAsync();
        seed.ChangeTracker.Clear();
        Assert.Equal("Пользователь не найден", (await commands.GrantDemoSubscriptionAsync("missing")).Error);

        context.UserSubscriptions.Add(new UserSubscription
        {
            UserId = user.Id,
            SubscriptionPlanId = demoPlan.Id,
            StartDate = DateTime.UtcNow.AddDays(-2),
            EndDate = DateTime.UtcNow.AddDays(5),
            SourceCode = "demo"
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal("Демо-тариф уже активен.", (await commands.GrantDemoSubscriptionAsync(user.Id)).Error);

        await context.UserSubscriptions.ExecuteDeleteAsync();
        context.ChangeTracker.Clear();
        context.UserSubscriptions.Add(new UserSubscription
        {
            UserId = user.Id,
            SubscriptionPlanId = paidPlan.Id,
            StartDate = DateTime.UtcNow.AddDays(-2),
            EndDate = DateTime.UtcNow.AddDays(5),
            SourceCode = "paid"
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal("У вас уже есть активная платная подписка.", (await commands.GrantDemoSubscriptionAsync(user.Id)).Error);

        await context.UserSubscriptions.ExecuteDeleteAsync();
        context.UserSubscriptionHistory.Add(new UserSubscriptionHistory
        {
            UserId = user.Id,
            SubscriptionPlanId = paidPlan.Id,
            StartDate = DateTime.UtcNow.AddDays(-40),
            EndDate = DateTime.UtcNow.AddDays(-10),
            ActualEndDate = DateTime.UtcNow.AddDays(-10),
            Status = "expired",
            SourceCode = "paid",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(
            "Демо-тариф доступен только тем, кто еще не покупал платные тарифы.",
            (await commands.GrantDemoSubscriptionAsync(user.Id)).Error);

        await context.UserSubscriptionHistory.ExecuteDeleteAsync();
        context.ChangeTracker.Clear();
        Assert.True((await commands.GrantDemoSubscriptionAsync(user.Id)).Success);
        await using var verification = fixture.CreateContext();
        var subscription = await verification.UserSubscriptions.SingleAsync();
        var history = await verification.UserSubscriptionHistory.SingleAsync(item => item.UserId == user.Id);
        Assert.Equal(demoPlan.Id, subscription.SubscriptionPlanId);
        Assert.Equal("demo", subscription.SourceCode);
        Assert.Equal("created", history.Status);
        Assert.Equal("Активирована демо-подписка", history.Comment);
    }

    [Fact]
    public async Task AdminCommandsPreserveValidationAndHistory()
    {
        await using var seed = fixture.CreateContext();
        var user = await this.CreateUserAsync(seed);
        await seed.SubscriptionPlans
            .Where(plan => plan.IsDemoPlan && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal)
            .ExecuteUpdateAsync(update => update.SetProperty(plan => plan.IsActive, false));
        var paidPlan = await this.CreatePersonalPlanAsync(seed, "Ручной тариф");
        var demoPlan = await this.CreatePersonalPlanAsync(seed, "Демо для администратора", isDemo: true, isActive: false);
        var defaultPlan = await this.CreatePersonalPlanAsync(seed, "Базовый", isDefault: true);
        var businessPlan = await fixture.CreatePlanAsync(seed, "Бизнесовый");
        await using var context = fixture.CreateContext();
        var commands = this.CreateService(context);

        Assert.Equal("Демо-тариф не настроен в системе", (await commands.GrantDemoSubscriptionByAdminAsync(user.Id)).Error);
        demoPlan.IsActive = true;
        await seed.SaveChangesAsync();
        seed.ChangeTracker.Clear();
        Assert.Equal("Пользователь не найден", (await commands.GrantDemoSubscriptionByAdminAsync("missing")).Error);
        Assert.True((await commands.GrantDemoSubscriptionByAdminAsync(user.Id)).Success);

        Assert.True((await commands.GrantDemoSubscriptionByAdminAsync(user.Id)).Success);
        Assert.Single(await context.UserSubscriptions.Where(item => item.UserId == user.Id).ToListAsync());

        Assert.Equal("Пользователь не найден", (await commands.AssignSubscriptionByAdminAsync("missing", paidPlan.Id, "Админ")).Error);
        Assert.Equal("Тарифный план не найден или неактивен", (await commands.AssignSubscriptionByAdminAsync(user.Id, int.MaxValue, "Админ")).Error);
        var inactivePlan = await this.CreatePersonalPlanAsync(context, "Неактивный", isActive: false);
        Assert.Equal("Тарифный план не найден или неактивен", (await commands.AssignSubscriptionByAdminAsync(user.Id, inactivePlan.Id, "Админ")).Error);
        Assert.Equal("Бизнес-тариф нельзя назначить пользователю напрямую.", (await commands.AssignSubscriptionByAdminAsync(user.Id, businessPlan.Id, "Админ")).Error);
        Assert.Equal("Базовый тариф нельзя назначить вручную", (await commands.AssignSubscriptionByAdminAsync(user.Id, defaultPlan.Id, "Админ")).Error);
        Assert.Equal("Демо-тариф выдавайте через отдельную кнопку", (await commands.AssignSubscriptionByAdminAsync(user.Id, demoPlan.Id, "Админ")).Error);
        Assert.True((await commands.AssignSubscriptionByAdminAsync(user.Id, paidPlan.Id, "Админ")).Success);

        Assert.Equal("Пользователь не найден", (await commands.ReturnUserToDefaultPlanByAdminAsync("missing", "Админ")).Error);
        Assert.True((await commands.ReturnUserToDefaultPlanByAdminAsync(user.Id, "Админ")).Success);
        await using var verification = fixture.CreateContext();
        Assert.Empty(await verification.UserSubscriptions.ToListAsync());
        var history = await verification.UserSubscriptionHistory
            .Where(item => item.UserId == user.Id)
            .OrderBy(item => item.Id)
            .ToListAsync();
        Assert.Equal(["created", "replaced", "admin_assigned", "admin_cancelled"], history.Select(item => item.Status));
        Assert.Contains("Тариф вручную заменен администратором", history[2].Comment);
        Assert.Null(history[3].ActualEndDate);
    }

    [Fact]
    public async Task AdminReturnFailsWhenThereIsNoCurrentSubscription()
    {
        await using var seed = fixture.CreateContext();
        var user = await this.CreateUserAsync(seed);
        await using var context = fixture.CreateContext();
        var commands = this.CreateService(context);

        Assert.Equal("У пользователя нет активного тарифа для снятия", (await commands.ReturnUserToDefaultPlanByAdminAsync(user.Id, "Админ")).Error);
    }

    [Fact]
    public async Task ExpiryIsAtomicAndRepeatedRunsDoNotDuplicateHistory()
    {
        await using var seed = fixture.CreateContext();
        var user = await this.CreateUserAsync(seed);
        var activeUser = await this.CreateUserAsync(seed);
        var plan = await this.CreatePersonalPlanAsync(seed, "Истекший");
        var now = DateTime.UtcNow;
        seed.UserSubscriptions.AddRange(
            new UserSubscription
            {
                UserId = user.Id,
                SubscriptionPlanId = plan.Id,
                StartDate = now.AddDays(-50),
                EndDate = now.AddDays(-1),
                SourceCode = "paid"
            },
            new UserSubscription
            {
                UserId = activeUser.Id,
                SubscriptionPlanId = plan.Id,
                StartDate = now.AddDays(-1),
                EndDate = now.AddDays(5),
                SourceCode = "manual"
            });
        await seed.SaveChangesAsync();
        await using var context = fixture.CreateContext();
        var commands = this.CreateService(context);
        Assert.Equal(1, await context.UserSubscriptions.CountAsync(item => item.UserId == user.Id && item.EndDate < DateTime.UtcNow));

        await commands.CheckAndDeactivateExpiredSubscriptionsAsync();
        Assert.Single(context.ChangeTracker.Entries<UserSubscriptionHistory>());
        await commands.CheckAndDeactivateExpiredSubscriptionsAsync();

        await using var verification = fixture.CreateContext();
        Assert.Empty(await verification.UserSubscriptions.Where(item => item.UserId == user.Id).ToListAsync());
        Assert.Single(await verification.UserSubscriptions.Where(item => item.UserId == activeUser.Id).ToListAsync());
        var history = await verification.UserSubscriptionHistory.SingleAsync(item => item.UserId == user.Id);
        Assert.Equal("expired", history.Status);
        Assert.NotNull(history.ActualEndDate);
    }

    [Fact]
    public async Task ConcurrentExpiryRunsCreateOnlyOneHistoryRecord()
    {
        await using var seed = fixture.CreateContext();
        var user = await this.CreateUserAsync(seed);
        var plan = await this.CreatePersonalPlanAsync(seed, "Параллельное истечение");
        seed.UserSubscriptions.Add(new UserSubscription
        {
            UserId = user.Id,
            SubscriptionPlanId = plan.Id,
            StartDate = DateTime.UtcNow.AddDays(-20),
            EndDate = DateTime.UtcNow.AddDays(-1),
            SourceCode = "paid"
        });
        await seed.SaveChangesAsync();
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var first = this.CreateService(firstContext);
        var second = this.CreateService(secondContext);

        await Task.WhenAll(first.CheckAndDeactivateExpiredSubscriptionsAsync(), second.CheckAndDeactivateExpiredSubscriptionsAsync());

        await using var verification = fixture.CreateContext();
        Assert.Empty(await verification.UserSubscriptions.ToListAsync());
        Assert.Single(await verification.UserSubscriptionHistory.ToListAsync());
    }

    [Fact]
    public async Task FailedExpirySaveRollsBackBothDeletionAndHistory()
    {
        await using var seed = fixture.CreateContext();
        var user = await this.CreateUserAsync(seed);
        var plan = await this.CreatePersonalPlanAsync(seed, "Ошибка истечения");
        seed.UserSubscriptions.Add(new UserSubscription
        {
            UserId = user.Id,
            SubscriptionPlanId = plan.Id,
            StartDate = DateTime.UtcNow.AddDays(-20),
            EndDate = DateTime.UtcNow.AddDays(-1),
            SourceCode = "paid"
        });
        await seed.SaveChangesAsync();
        await using var context = fixture.CreateContext(new FirstSaveFailureInterceptor());
        var commands = this.CreateService(context);

        await Assert.ThrowsAsync<DbUpdateException>(() => commands.CheckAndDeactivateExpiredSubscriptionsAsync());
        await using var verification = fixture.CreateContext();
        Assert.Single(await verification.UserSubscriptions.Where(item => item.UserId == user.Id).ToListAsync());
        Assert.Empty(await verification.UserSubscriptionHistory.Where(item => item.UserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task FailedWriteRollsBackSubscriptionAndHistoryAndReturnsLegacyError()
    {
        await using var seed = fixture.CreateContext();
        var user = await this.CreateUserAsync(seed, canUseTestSubscriptions: true);
        var plan = await this.CreatePersonalPlanAsync(seed, "Ошибка сохранения");
        var interceptor = new SecondSaveFailureInterceptor();
        await using var context = fixture.CreateContext(interceptor);
        var commands = this.CreateService(context);

        var result = await commands.ActivateSubscriptionForTestingAsync(user.Id, plan.Id);

        Assert.False(result.Success);
        Assert.Equal("Ошибка при тестовой активации тарифа", result.Error);
        await using var verification = fixture.CreateContext();
        Assert.Empty(await verification.UserSubscriptions.ToListAsync());
        Assert.Empty(await verification.UserSubscriptionHistory.Where(item => item.UserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task TransientCommitFailureRetriesWithoutCreatingDuplicateHistory()
    {
        await using var seed = fixture.CreateContext();
        var user = await this.CreateUserAsync(seed, canUseTestSubscriptions: true);
        var plan = await this.CreatePersonalPlanAsync(seed, "Повтор при фиксации");
        var failure = new CommitFailureInterceptor(afterCommit: false);
        await using var context = fixture.CreateContext(failure);
        var commands = this.CreateService(context);

        var result = await commands.ActivateSubscriptionForTestingAsync(user.Id, plan.Id);

        Assert.True(result.Success);
        Assert.Equal(1, failure.FailureCount);
        Assert.Equal(2, failure.CommitAttempts);
        await using var verification = fixture.CreateContext();
        Assert.Single(await verification.UserSubscriptions.ToListAsync());
        Assert.Single(await verification.UserSubscriptionHistory.ToListAsync());
    }

    [Theory]
    [InlineData("activate")]
    [InlineData("extend")]
    [InlineData("assign")]
    [InlineData("expire")]
    public async Task CommitAcknowledgementFailureDoesNotRepeatCommittedSubscriptionCommand(string command)
    {
        await using var seed = fixture.CreateContext();
        var user = await this.CreateUserAsync(seed, canUseTestSubscriptions: true);
        var plan = await this.CreatePersonalPlanAsync(seed, $"Повтор после commit {command}");
        if (command is "extend" or "expire")
        {
            seed.UserSubscriptions.Add(new UserSubscription
            {
                UserId = user.Id,
                SubscriptionPlanId = plan.Id,
                StartDate = DateTime.UtcNow.AddDays(-20),
                EndDate = command == "expire" ? DateTime.UtcNow.AddDays(-1) : DateTime.UtcNow.AddDays(4),
                SourceCode = "manual"
            });
            await seed.SaveChangesAsync();
        }

        var failure = new CommitFailureInterceptor(afterCommit: true);
        await using var context = fixture.CreateContext(failure);
        var commands = this.CreateService(context);

        if (command == "activate")
        {
            Assert.True((await commands.ActivateSubscriptionForTestingAsync(user.Id, plan.Id)).Success);
        }
        else if (command == "extend")
        {
            Assert.True((await commands.ExtendSubscriptionForTestingAsync(user.Id)).Success);
        }
        else if (command == "assign")
        {
            Assert.True((await commands.AssignSubscriptionByAdminAsync(user.Id, plan.Id, "Админ")).Success);
        }
        else
        {
            await commands.CheckAndDeactivateExpiredSubscriptionsAsync();
        }

        Assert.Equal(1, failure.FailureCount);
        Assert.Equal(1, failure.CommitAttempts);
        await using var verification = fixture.CreateContext();
        Assert.Equal(command == "expire" ? 0 : 1, await verification.UserSubscriptions.CountAsync(item => item.UserId == user.Id));
        Assert.Single(await verification.UserSubscriptionHistory.Where(item => item.UserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task CommandRejectsAmbientTransactionBeforeChangingCallerState()
    {
        await using var seed = fixture.CreateContext();
        var user = await this.CreateUserAsync(seed, canUseTestSubscriptions: true);
        var plan = await this.CreatePersonalPlanAsync(seed, "Внешняя транзакция");
        await using var context = fixture.CreateContext();
        var commands = this.CreateService(context);
        var trackedUser = await context.Users.SingleAsync(item => item.Id == user.Id);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var result = await commands.ActivateSubscriptionForTestingAsync(user.Id, plan.Id);

        Assert.False(result.Success);
        Assert.Equal("Ошибка при тестовой активации тарифа", result.Error);
        Assert.Same(trackedUser, context.ChangeTracker.Entries<ApplicationUser>().Single().Entity);
        Assert.Empty(context.ChangeTracker.Entries<UserSubscription>());
        await transaction.RollbackAsync();
    }

    private async Task<ApplicationUser> CreateUserAsync(OpenCRMContext context, bool canUseTestSubscriptions = true)
    {
        var user = new ApplicationUser
        {
            Email = $"subscription-command-{Guid.NewGuid():N}@example.test",
            CanUseTestSubscriptions = canUseTestSubscriptions
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user;
    }

    private async Task<SubscriptionPlan> CreatePersonalPlanAsync(
        OpenCRMContext context,
        string name,
        bool isActive = true,
        bool isDefault = false,
        bool isDemo = false)
    {
        var plan = await fixture.CreatePlanAsync(context, name, isDefault, isDemo);
        plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        plan.IsActive = isActive;
        await context.SaveChangesAsync();

        return plan;
    }

    private PersonalSubscriptionCommandService CreateService(OpenCRMContext context) =>
        new(context, NullLogger<PersonalSubscriptionCommandService>.Instance,
            Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == true));

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class SecondSaveFailureInterceptor : SaveChangesInterceptor
    {
        private int _saveCount;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            this._saveCount++;
            if (this._saveCount == 2)
            {
                throw new DbUpdateException("Тестовый сбой записи истории подписки.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class FirstSaveFailureInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<int>>(new DbUpdateException("Тестовый сбой сохранения истечения подписки."));
    }

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
            if (!afterCommit && this.FailureCount == 0)
            {
                this.FailureCount++;
                throw new PostgresException("Тестовый временный сбой фиксации.", "ERROR", "ERROR", "40001");
            }

            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (afterCommit && this.FailureCount == 0)
            {
                this.FailureCount++;
                throw new PostgresException("Тестовый сбой подтверждения фиксации.", "ERROR", "ERROR", "40001");
            }

            return Task.CompletedTask;
        }
    }
}
