using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class PersonalSubscriptionActivationTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task NewPurchaseCreatesPaidSubscriptionHistoryAndOutboxAtomically()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context);
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>();

        var result = await service.ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.True(result.Success);
        var subscription = await context.UserSubscriptions.AsNoTracking().SingleAsync(item => item.UserId == user.Id);
        Assert.Equal(plan.Id, subscription.SubscriptionPlanId);
        Assert.Equal("paid", subscription.SourceCode);
        Assert.False(subscription.AutoRenewal);
        var history = await context.UserSubscriptionHistory.AsNoTracking().SingleAsync(item => item.UserId == user.Id);
        Assert.Equal("created", history.Status);
        Assert.Equal("paid", history.SourceCode);
        Assert.Contains(payment.ExternalPaymentId!, history.Comment);
        var completedPayment = await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id);
        Assert.Equal("completed", completedPayment.PaymentStatus);
        Assert.NotNull(completedPayment.StatusUpdatedAt);
        var expectedPaidAt = payment.PaidAt!.Value;
        expectedPaidAt = new DateTime(expectedPaidAt.Ticks - expectedPaidAt.Ticks % 10, DateTimeKind.Utc);
        Assert.Equal(expectedPaidAt, completedPayment.PaidAt);
        var notification = await context.TelegramNotificationOutbox.AsNoTracking().SingleAsync();
        Assert.Equal($"subscription-paid:{payment.ExternalPaymentId}", notification.NotificationKey);
    }

    [Fact]
    public async Task NewPurchaseReplacesActiveSubscriptionAndRecordsReplacementSnapshot()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context);
        var oldPlan = await this.CreatePersonalPlanAsync(context, "Старый", 2);
        var oldSubscription = new UserSubscription
        {
            UserId = user.Id,
            SubscriptionPlanId = oldPlan.Id,
            StartDate = DateTime.UtcNow.AddDays(-20),
            EndDate = DateTime.UtcNow.AddDays(2),
            SourceCode = "manual",
            AutoRenewal = true
        };
        context.UserSubscriptions.Add(oldSubscription);
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.True(result.Success);
        var subscription = await context.UserSubscriptions.AsNoTracking().SingleAsync(item => item.UserId == user.Id);
        Assert.Equal(plan.Id, subscription.SubscriptionPlanId);
        Assert.False(subscription.AutoRenewal);
        var history = await context.UserSubscriptionHistory.AsNoTracking().SingleAsync(item => item.UserId == user.Id);
        Assert.Equal("replaced", history.Status);
        Assert.Equal(plan.Id, history.SubscriptionPlanId);
        Assert.Equal("paid", history.SourceCode);
    }

    [Fact]
    public async Task NewPurchaseArchivesExpiredSubscriptionBeforeCreatingCurrentOneAndPreservesGrants()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context);
        var oldPlan = await this.CreatePersonalPlanAsync(context, "Истёкший", 1);
        var timestamp = DateTime.UtcNow;
        var expiredAt = new DateTime(timestamp.Ticks - timestamp.Ticks % 10, DateTimeKind.Utc).AddDays(-2);
        context.UserSubscriptions.Add(new UserSubscription
        {
            UserId = user.Id,
            SubscriptionPlanId = oldPlan.Id,
            StartDate = expiredAt.AddDays(-30),
            EndDate = expiredAt,
            SourceCode = "paid"
        });
        var feature = await fixture.CreateFeatureAsync(context, $"grant-{Guid.NewGuid():N}", "Личная функция",
            FeatureValueType.Boolean, FeatureCatalogCodes.Scopes.Personal);
        var grant = new UserFeatureGrant
        {
            UserId = user.Id,
            FeatureDefinitionId = feature.Id,
            BoolValue = true,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            Source = "manual"
        };
        context.UserFeatureGrants.Add(grant);
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.True(result.Success);
        Assert.Equal(plan.Id, (await context.UserSubscriptions.AsNoTracking().SingleAsync(item => item.UserId == user.Id)).SubscriptionPlanId);
        var history = await context.UserSubscriptionHistory.AsNoTracking().Where(item => item.UserId == user.Id).OrderBy(item => item.Id).ToArrayAsync();
        Assert.Equal(new[] { "expired", "created" }, history.Select(item => item.Status));
        Assert.Equal(oldPlan.Id, history[0].SubscriptionPlanId);
        Assert.Equal(expiredAt, history[0].EndDate);
        Assert.NotNull(history[0].ActualEndDate);
        var persistedGrant = await context.UserFeatureGrants.AsNoTracking().SingleAsync(item => item.Id == grant.Id);
        Assert.Equal(grant.BoolValue, persistedGrant.BoolValue);
        Assert.Equal(grant.IsEnabled, persistedGrant.IsEnabled);
        Assert.Equal(grant.Source, persistedGrant.Source);
    }

    [Fact]
    public async Task CompletedPaymentReplayDoesNotDuplicateSubscriptionHistoryOrOutbox()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context, status: "completed");
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>();

        var result = await service.ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.True(result.Success);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
        Assert.Empty(await context.UserSubscriptionHistory.ToListAsync());
        Assert.Empty(await context.TelegramNotificationOutbox.ToListAsync());
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("failed")]
    [InlineData("refunded")]
    public async Task ClosedPaymentIsRejectedWithoutChangingItsStatus(string status)
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context, status: status);
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal(status, (await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id)).PaymentStatus);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
    }

    [Theory]
    [InlineData("pending", false)]
    [InlineData("processing", true)]
    [InlineData("waiting_for_capture", true)]
    [InlineData("paid_waiting_confirmation", true)]
    public async Task PendingPaymentRequiresProviderPaidAtAndPaidProcessingCanResume(string status, bool isPaid)
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context, status: status, isPaid: isPaid);
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.Equal(isPaid, result.Success);
        Assert.Equal(isPaid ? "completed" : status,
            (await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id)).PaymentStatus);
        Assert.Equal(isPaid, await context.UserSubscriptions.AnyAsync(item => item.UserId == user.Id));
    }

    [Fact]
    public async Task PaymentWithoutProviderPaidAtIsRejectedWithoutInvalidatingPendingPayment()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context, isPaid: false);
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        var persistedPayment = await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id);
        Assert.Equal("pending", persistedPayment.PaymentStatus);
        Assert.Null(persistedPayment.PaidAt);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task UnknownPaidPaymentStatusIsRejectedWithoutChangingPayment()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context, status: "authorized");
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("authorized", (await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id)).PaymentStatus);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task LegacyPaymentWithMissingUserIsMarkedFailed()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context);
        await context.Database.OpenConnectionAsync();
        try
        {
            await context.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM identity.\"AspNetUsers\" WHERE \"Id\" = {user.Id}");
        }
        finally
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync("SET session_replication_role = origin");
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }
        }

        context.ChangeTracker.Clear();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id)).PaymentStatus);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task LegacyPaymentWithMissingPlanIsMarkedFailed()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context);
        await context.Database.OpenConnectionAsync();
        try
        {
            await context.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"SubscriptionPlans\" WHERE \"Id\" = {plan.Id}");
        }
        finally
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync("SET session_replication_role = origin");
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }
        }

        context.ChangeTracker.Clear();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id)).PaymentStatus);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task InactivePlanMarksOwnedPaidPaymentFailed()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context);
        plan.IsActive = false;
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id)).PaymentStatus);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task DemoPlanPurchaseMarksOwnedPaidPaymentFailed()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context, isDemo: true);
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id)).PaymentStatus);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task PaymentMustMatchOwnerOperationAndPlanBeforeCompletedReplay()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context, status: "completed");
        var otherUser = new ApplicationUser();
        var otherPlan = await this.CreatePersonalPlanAsync(context, "Другой", 8);
        context.Users.Add(otherUser);
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>();

        var wrongOwner = await service.ActivateNewSubscriptionAsync(otherUser.Id, plan.Id, payment.ExternalPaymentId!);
        var wrongPlan = await service.ActivateNewSubscriptionAsync(user.Id, otherPlan.Id, payment.ExternalPaymentId!);
        var wrongOperation = await service.ActivateExtendedSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(wrongOwner.Success);
        Assert.False(wrongPlan.Success);
        Assert.False(wrongOperation.Success);
        Assert.Equal("completed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id)).PaymentStatus);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task DuplicateExternalPaymentIdFailsClosedWithoutChangingEitherPayment()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context);
        var duplicate = new SubscriptionPayment
        {
            UserId = user.Id,
            SubscriptionPlanId = plan.Id,
            Amount = payment.Amount,
            CreatedAt = DateTime.UtcNow,
            PaymentMethod = payment.PaymentMethod,
            PaymentStatus = "processing",
            PaidAt = DateTime.UtcNow,
            ExternalPaymentId = payment.ExternalPaymentId
        };
        // Проверяем защиту от повреждённой истории до появления уникального индекса.
        await context.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_SubscriptionPayments_ExternalPaymentId\"");
        try
        {
            context.SubscriptionPayments.Add(duplicate);
            await context.SaveChangesAsync();
            using var provider = this.CreateProvider(context);
            using var scope = provider.CreateScope();

            var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
                .ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

            Assert.False(result.Success);
            Assert.Equal(new[] { "pending", "processing" },
                await context.SubscriptionPayments.AsNoTracking().OrderBy(item => item.Id).Select(item => item.PaymentStatus).ToArrayAsync());
            Assert.Empty(await context.UserSubscriptions.ToListAsync());
        }
        finally
        {
            await fixture.ResetDatabaseAsync();
            await context.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX \"IX_SubscriptionPayments_ExternalPaymentId\" ON \"SubscriptionPayments\" (\"ExternalPaymentId\") WHERE \"ExternalPaymentId\" IS NOT NULL");
        }
    }

    [Fact]
    public async Task MissingPaymentIsRejected()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        var plan = await this.CreatePersonalPlanAsync(context, "Личный", 3);
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(user.Id, plan.Id, Guid.NewGuid().ToString("N"));

        Assert.False(result.Success);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task BusinessRejectionCommitsFailedPaymentWithoutCreatingSubscription()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context);
        plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business;
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        var failedPayment = await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id);
        Assert.Equal("failed", failedPayment.PaymentStatus);
        Assert.NotNull(failedPayment.StatusUpdatedAt);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
        Assert.Empty(await context.TelegramNotificationOutbox.ToListAsync());
    }

    [Fact]
    public async Task ExtensionUsesCurrentEndDateAndAddsHistoryAndOutbox()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context, operationType: "yookassa_extension");
        var timestamp = DateTime.UtcNow;
        var oldEndDate = new DateTime(timestamp.Ticks - timestamp.Ticks % 10, DateTimeKind.Utc).AddDays(5);
        var current = new UserSubscription
        {
            UserId = user.Id,
            SubscriptionPlanId = plan.Id,
            StartDate = oldEndDate.AddDays(-30),
            EndDate = oldEndDate,
            SourceCode = "paid"
        };
        context.UserSubscriptions.Add(current);
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
            .ActivateExtendedSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.True(result.Success);
        var extended = await context.UserSubscriptions.AsNoTracking().SingleAsync(item => item.UserId == user.Id);
        Assert.Equal(oldEndDate.AddDays(plan.DurationDays), extended.EndDate);
        var history = await context.UserSubscriptionHistory.AsNoTracking().SingleAsync(item => item.UserId == user.Id);
        Assert.Equal("extended", history.Status);
        Assert.Equal(extended.EndDate, history.EndDate);
        Assert.Contains(oldEndDate.ToString("dd.MM.yyyy"), history.Comment);
        Assert.Single(await context.TelegramNotificationOutbox.ToListAsync());
    }

    [Fact]
    public async Task ExtensionBusinessRejectionsCommitFailedPayment()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context, operationType: "yookassa_extension");
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
            .ActivateExtendedSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id)).PaymentStatus);
        Assert.Empty(await context.UserSubscriptions.ToListAsync());
        Assert.Empty(await context.UserSubscriptionHistory.ToListAsync());
    }

    [Fact]
    public async Task ExtensionRejectsWhenCurrentSubscriptionPlanDiffersFromPaidPlan()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context, operationType: "yookassa_extension");
        var currentPlan = await this.CreatePersonalPlanAsync(context, "Другой активный", 6);
        context.UserSubscriptions.Add(new UserSubscription
        {
            UserId = user.Id,
            SubscriptionPlanId = currentPlan.Id,
            StartDate = DateTime.UtcNow.AddDays(-25),
            EndDate = DateTime.UtcNow.AddDays(2),
            SourceCode = "paid"
        });
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>()
            .ActivateExtendedSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id)).PaymentStatus);
        Assert.Equal(currentPlan.Id, (await context.UserSubscriptions.AsNoTracking().SingleAsync(item => item.UserId == user.Id)).SubscriptionPlanId);
        Assert.Empty(await context.UserSubscriptionHistory.ToListAsync());
    }

    [Fact]
    public async Task ExtensionRejectsDemoMismatchedPlanAndTooEarlyRenewal()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(
            context, operationType: "yookassa_extension", status: "processing", isDemo: true);
        var current = new UserSubscription
        {
            UserId = user.Id,
            SubscriptionPlanId = plan.Id,
            StartDate = DateTime.UtcNow.AddDays(-2),
            EndDate = DateTime.UtcNow.AddDays(2),
            SourceCode = "demo"
        };
        context.UserSubscriptions.Add(current);
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>();

        var otherPlan = await this.CreatePersonalPlanAsync(context, "Другой", 9);
        var mismatch = await service.ActivateExtendedSubscriptionAsync(user.Id, otherPlan.Id, payment.ExternalPaymentId!);
        Assert.False(mismatch.Success);
        Assert.Equal("processing", (await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id)).PaymentStatus);

        var demo = await service.ActivateExtendedSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!);
        Assert.False(demo.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id)).PaymentStatus);

        await context.SubscriptionPayments.Where(item => item.Id == payment.Id).ExecuteUpdateAsync(update => update
            .SetProperty(item => item.PaymentStatus, "processing")
            .SetProperty(item => item.SubscriptionPlanId, otherPlan.Id));
        await context.UserSubscriptions.Where(item => item.UserId == user.Id).ExecuteUpdateAsync(update => update
            .SetProperty(item => item.SubscriptionPlanId, otherPlan.Id)
            .SetProperty(item => item.EndDate, DateTime.UtcNow.AddDays(20)));
        await context.SaveChangesAsync();

        var tooEarly = await service.ActivateExtendedSubscriptionAsync(user.Id, otherPlan.Id, payment.ExternalPaymentId!);
        Assert.False(tooEarly.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id)).PaymentStatus);
        Assert.Empty(await context.UserSubscriptionHistory.ToListAsync());
    }

    [Fact]
    public async Task ActivationRejectsExternalTransactionAndPendingContextWritesBeforeMutation()
    {
        await using var context = fixture.CreateContext();
        var (user, plan, payment) = await this.CreatePaymentAsync(context);
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPersonalSubscriptionActivationService>();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!));
            await transaction.RollbackAsync();
        }

        context.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Name = "Ожидающая запись",
            Description = "Не должна попасть в транзакцию активации",
            Price = 1,
            DurationDays = 1,
            VehicleLimit = 1,
            Order = 1
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ActivateNewSubscriptionAsync(user.Id, plan.Id, payment.ExternalPaymentId!));
        Assert.Equal("pending", (await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id)).PaymentStatus);
        context.ChangeTracker.Clear();
    }

    private async Task<(ApplicationUser User, SubscriptionPlan Plan, SubscriptionPayment Payment)> CreatePaymentAsync(
        OpenCRMContext context,
        string operationType = "yookassa_purchase",
        string status = "pending",
        bool isPaid = true,
        bool isDemo = false)
    {
        var user = new ApplicationUser { Email = $"activation-{Guid.NewGuid():N}@example.test" };
        context.Users.Add(user);
        var plan = await this.CreatePersonalPlanAsync(context, $"Личный-{Guid.NewGuid():N}", 5, isDemo);
        var payment = new SubscriptionPayment
        {
            UserId = user.Id,
            SubscriptionPlanId = plan.Id,
            Amount = plan.EffectivePrice,
            CreatedAt = DateTime.UtcNow,
            PaymentMethod = operationType,
            PaymentStatus = status,
            PaidAt = isPaid ? DateTime.UtcNow : null,
            ExternalPaymentId = Guid.NewGuid().ToString("N")
        };
        context.SubscriptionPayments.Add(payment);
        await context.SaveChangesAsync();

        return (user, plan, payment);
    }

    private async Task<SubscriptionPlan> CreatePersonalPlanAsync(
        OpenCRMContext context,
        string name,
        int order,
        bool isDemo = false)
    {
        var plan = await fixture.CreatePlanAsync(context, name, isDemo: isDemo);
        plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        plan.Order = order;
        await context.SaveChangesAsync();

        return plan;
    }

    private ServiceProvider CreateProvider(OpenCRMContext context)
    {
        context.ChangeTracker.Clear();

        return fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
    }
}
