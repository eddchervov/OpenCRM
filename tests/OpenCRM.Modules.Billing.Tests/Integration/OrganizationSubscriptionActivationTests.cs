using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class OrganizationSubscriptionActivationTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task NewPurchaseReplacesActiveDemoAndWritesAuditOutboxAndPaymentAtomically()
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, payment) = await this.CreatePaymentAsync(context);
        var demoPlan = await fixture.CreatePlanAsync(context, "Демо до оплаты", isDemo: true);
        var oldEndDate = DateTime.UtcNow.AddDays(4);
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = demoPlan.Id,
            StartDate = oldEndDate.AddDays(-30),
            EndDate = oldEndDate,
            SourceCode = "demo"
        });
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.True(result.Success);

        var subscriptions = await context.OrganizationSubscriptions.AsNoTracking()
            .Where(subscription => subscription.OrganizationId == organization.Id)
            .OrderBy(subscription => subscription.Id)
            .ToArrayAsync();

        Assert.Equal(2, subscriptions.Length);
        Assert.True(subscriptions[0].EndDate < oldEndDate);
        Assert.Equal(plan.Id, subscriptions[1].SubscriptionPlanId);
        Assert.Equal("paid", subscriptions[1].SourceCode);
        Assert.False(subscriptions[1].AutoRenewal);
        Assert.Equal("completed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaymentStatus);
        Assert.NotNull((await context.SubscriptionPayments.AsNoTracking().SingleAsync()).StatusUpdatedAt);
        var audit = await context.OrganizationAuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal("billing.subscription.activated", audit.EventType);
        Assert.Equal(organization.OwnerUserId, audit.ActorUserId);
        Assert.Equal(plan.Id.ToString(), audit.TargetId);
        Assert.Equal("subscription-paid:" + payment.ExternalPaymentId,
            (await context.TelegramNotificationOutbox.AsNoTracking().SingleAsync()).NotificationKey);
        Assert.Empty(await context.UserSubscriptionHistory.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CompletedPaymentReplaySucceedsWithoutRecheckingOrganizationAccess()
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, payment) = await this.CreatePaymentAsync(context, status: "completed");
        var permissions = OrganizationSubscriptionActivationTests.CreatePermissionService(false);
        using var provider = this.CreateProvider(context, permissions.Object);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.True(result.Success);
        permissions.Verify(service => service.HasPermissionAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        Assert.Empty(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
        Assert.Empty(await context.OrganizationAuditEvents.AsNoTracking().ToListAsync());
        Assert.Empty(await context.TelegramNotificationOutbox.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PaymentMustMatchOwnerPlanOrganizationAndOperationBeforeCompletedReplay()
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, payment) = await this.CreatePaymentAsync(context, status: "completed");
        var otherOrganization = await fixture.CreateOrganizationAsync(context);
        var otherPlan = await fixture.CreatePlanAsync(context, "Другой план");
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>();

        var wrongActor = await service.ActivateNewSubscriptionAsync("not-owner", organization.Id, plan.Id, payment.ExternalPaymentId!);
        var wrongPlan = await service.ActivateNewSubscriptionAsync(organization.OwnerUserId, organization.Id, otherPlan.Id, payment.ExternalPaymentId!);
        var wrongOrganization = await service.ActivateNewSubscriptionAsync(organization.OwnerUserId, otherOrganization.Id, plan.Id, payment.ExternalPaymentId!);
        var wrongOperation = await service.ActivateExtendedSubscriptionAsync(organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(wrongActor.Success);
        Assert.False(wrongPlan.Success);
        Assert.False(wrongOrganization.Success);
        Assert.False(wrongOperation.Success);
        Assert.Equal("completed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaymentStatus);
        Assert.Empty(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("failed")]
    [InlineData("refunded")]
    public async Task TerminalPaymentIsRejectedWithoutChangingStatus(string status)
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, payment) = await this.CreatePaymentAsync(context, status: status);
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal(status, (await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaymentStatus);
        Assert.Empty(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("pending", false)]
    [InlineData("processing", true)]
    [InlineData("waiting_for_capture", true)]
    [InlineData("paid_waiting_confirmation", true)]
    public async Task ProviderPaidAtAndSupportedStatusAreRequired(string status, bool isPaid)
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, payment) = await this.CreatePaymentAsync(context, status: status, isPaid: isPaid);
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.Equal(isPaid, result.Success);
        Assert.Equal(isPaid ? "completed" : status,
            (await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaymentStatus);
        Assert.Equal(isPaid, await context.OrganizationSubscriptions.AnyAsync());
    }

    [Fact]
    public async Task UnknownPaidStatusIsRejectedWithoutChangingPayment()
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, payment) = await this.CreatePaymentAsync(context, status: "authorized");
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("authorized", (await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaymentStatus);
        Assert.Empty(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task MissingOrAmbiguousPaymentIsRejected()
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, payment) = await this.CreatePaymentAsync(context);

        var duplicate = new SubscriptionPayment
        {
            UserId = payment.UserId,
            SubscriptionPlanId = payment.SubscriptionPlanId,
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
            var service = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>();

            var ambiguous = await service.ActivateNewSubscriptionAsync(
                organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

            var missing = await service.ActivateNewSubscriptionAsync(organization.OwnerUserId, organization.Id, plan.Id, Guid.NewGuid().ToString("N"));

            Assert.False(ambiguous.Success);
            Assert.False(missing.Success);
            Assert.Equal(new[] { "pending", "processing" },
                await context.SubscriptionPayments.AsNoTracking().OrderBy(item => item.Id).Select(item => item.PaymentStatus).ToArrayAsync());
            Assert.Empty(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
        }
        finally
        {
            await fixture.ResetDatabaseAsync();
            await context.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX \"IX_SubscriptionPayments_ExternalPaymentId\" ON \"SubscriptionPayments\" (\"ExternalPaymentId\") WHERE \"ExternalPaymentId\" IS NOT NULL");
        }
    }

    [Fact]
    public async Task MissingOrganizationMemberFailsPaidPayment()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Для участника другого рабочего пространства");
        var otherUser = new ApplicationUser { Email = $"not-member-{Guid.NewGuid():N}@example.test" };
        context.Users.Add(otherUser);
        await context.SaveChangesAsync();

        var payment = await this.CreateExistingPaymentAsync(
            context, otherUser.Id, plan.Id, organization.Id, "yookassa_workspace_purchase:");

        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(otherUser.Id, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaymentStatus);
        Assert.NotNull((await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaidAt);
        Assert.Empty(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("archived")]
    public async Task InactiveOrUnavailableOrganizationFailsPaidPayment(string state)
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, payment) = await this.CreatePaymentAsync(context);
        switch (state)
        {
            case "inactive":
                organization.IsActive = false;
                break;
            case "deleted":
                organization.IsDeleted = true;
                break;
            case "archived":
                organization.IsArchived = true;
                break;
        }

        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaymentStatus);
        Assert.Empty(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task DeniedBillingPermissionFailsPaidPayment()
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, payment) = await this.CreatePaymentAsync(context);
        var permissionService = OrganizationSubscriptionActivationTests.CreatePermissionService(false);
        using var provider = this.CreateProvider(context, permissionService.Object);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaymentStatus);
        Assert.Empty(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task MissingOrganizationFailsPaidPayment()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = $"orphan-org-{Guid.NewGuid():N}@example.test" };
        context.Users.Add(user);
        var plan = await fixture.CreatePlanAsync(context, "Для удаленной организации");
        await context.SaveChangesAsync();
        var missingOrganizationId = Guid.NewGuid();

        var payment = await this.CreateExistingPaymentAsync(
            context, user.Id, plan.Id, missingOrganizationId, "yookassa_workspace_purchase:");

        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(user.Id, missingOrganizationId, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaymentStatus);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PurchaseRejectsUnavailableTariffsAndActivePaidPlans(bool isDefault, bool isDemo)
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, payment) = await this.CreatePaymentAsync(context, isDemo: isDemo);
        plan.IsDefaultPlan = isDefault;
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaymentStatus);
        Assert.Empty(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
        Assert.Empty(await context.OrganizationAuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PurchaseRejectsInactivePlanAndPolicyRejectsMissingPlan()
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, payment) = await this.CreatePaymentAsync(context);
        plan.IsActive = false;
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var activation = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        var policy = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionPurchasePolicyService>()
            .CanPurchasePlanAsync(organization.OwnerUserId, organization.Id, int.MaxValue);

        Assert.False(activation.Success);
        Assert.False(policy.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaymentStatus);
    }

    [Fact]
    public async Task LegacyPaymentWithMissingPlanIsMarkedFailed()
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, payment) = await this.CreatePaymentAsync(context);
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

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaymentStatus);
        Assert.Empty(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ExtensionUpdatesDateAndWritesSingleAuditAndOutbox()
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, payment) = await this.CreatePaymentAsync(context, operation: "extension");
        var oldEndDate = DateTime.UtcNow.AddDays(3);
        oldEndDate = new DateTime(oldEndDate.Ticks - oldEndDate.Ticks % 10, DateTimeKind.Utc);
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = plan.Id,
            StartDate = oldEndDate.AddDays(-30),
            EndDate = oldEndDate,
            SourceCode = "paid"
        });
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateExtendedSubscriptionAsync(organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.True(result.Success);
        Assert.Equal(oldEndDate.AddDays(plan.DurationDays),
            (await context.OrganizationSubscriptions.AsNoTracking().SingleAsync()).EndDate);
        Assert.Equal("billing.subscription.extended", (await context.OrganizationAuditEvents.AsNoTracking().SingleAsync()).EventType);
        Assert.Single(await context.TelegramNotificationOutbox.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ExtensionBusinessRejectionsCommitFailedPaymentWithoutSubscriptionChanges()
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, payment) = await this.CreatePaymentAsync(context, operation: "extension");
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateExtendedSubscriptionAsync(organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaymentStatus);
        Assert.NotNull((await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaidAt);
        Assert.Empty(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
        Assert.Empty(await context.OrganizationAuditEvents.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("demo", false, false)]
    [InlineData("default", true, false)]
    [InlineData("paid", false, true)]
    [InlineData("paid", false, false)]
    public async Task ExtensionRejectsDemoDefaultPlanMismatchAndTooEarly(string source, bool isDefault, bool isMismatch)
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, payment) = await this.CreatePaymentAsync(context, operation: "extension");

        var currentPlan = isMismatch
            ? await fixture.CreatePlanAsync(context, "Другой активный")
            : plan;

        currentPlan.IsDemoPlan = source == "demo";
        currentPlan.IsDefaultPlan = isDefault;
        var endDate = source == "paid" ? DateTime.UtcNow.AddDays(20) : DateTime.UtcNow.AddDays(2);
        endDate = new DateTime(endDate.Ticks - endDate.Ticks % 10, DateTimeKind.Utc);
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = currentPlan.Id,
            StartDate = endDate.AddDays(-30),
            EndDate = endDate,
            SourceCode = source
        });
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateExtendedSubscriptionAsync(organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.False(result.Success);
        Assert.Equal("failed", (await context.SubscriptionPayments.AsNoTracking().SingleAsync()).PaymentStatus);
        Assert.Equal(endDate, (await context.OrganizationSubscriptions.AsNoTracking().SingleAsync()).EndDate);
        Assert.Empty(await context.OrganizationAuditEvents.AsNoTracking().ToListAsync());

        if (source == "paid" && !isMismatch)
        {
            Assert.Equal("Продление откроется за 7 дней до окончания. Осталось дней: 20.", result.Error);
        }
    }

    [Fact]
    public async Task PolicyPreservesOrganizationPlanRulesAndAccessFailures()
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, _) = await this.CreatePaymentAsync(context);
        var inactive = await fixture.CreatePlanAsync(context, "Неактивный");
        inactive.IsActive = false;
        var personalPlan = await fixture.CreatePlanAsync(context, "Личный тариф");
        personalPlan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        var defaultPlan = await fixture.CreatePlanAsync(context, "Базовый", isDefault: true);
        var demoPlan = await fixture.CreatePlanAsync(context, "Демо", isDemo: true);
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();
        var policy = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionPurchasePolicyService>();

        Assert.True((await policy.CanPurchasePlanAsync(organization.OwnerUserId, organization.Id, plan.Id)).Success);
        Assert.False((await policy.CanPurchasePlanAsync(organization.OwnerUserId, organization.Id, inactive.Id)).Success);
        Assert.False((await policy.CanPurchasePlanAsync(organization.OwnerUserId, organization.Id, personalPlan.Id)).Success);
        Assert.False((await policy.CanPurchasePlanAsync(organization.OwnerUserId, organization.Id, defaultPlan.Id)).Success);
        Assert.False((await policy.CanPurchasePlanAsync(organization.OwnerUserId, organization.Id, demoPlan.Id)).Success);

        var same = await fixture.CreatePlanAsync(context, "Текущий");
        var other = await fixture.CreatePlanAsync(context, "Другой");
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = same.Id,
            StartDate = DateTime.UtcNow.AddDays(-5),
            EndDate = DateTime.UtcNow.AddDays(5),
            SourceCode = "paid"
        });
        await context.SaveChangesAsync();
        Assert.False((await policy.CanPurchasePlanAsync(organization.OwnerUserId, organization.Id, same.Id)).Success);
        Assert.False((await policy.CanPurchasePlanAsync(organization.OwnerUserId, organization.Id, other.Id)).Success);

        var otherOrganization = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.CanPurchasePlanAsync(organization.OwnerUserId, otherOrganization, plan.Id));
        var noPermission = OrganizationSubscriptionActivationTests.CreatePermissionService(false);
        using var deniedProvider = this.CreateProvider(context, noPermission.Object);
        using var deniedScope = deniedProvider.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            deniedScope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionPurchasePolicyService>()
                .CanPurchasePlanAsync(organization.OwnerUserId, organization.Id, plan.Id));
    }

    [Fact]
    public async Task PolicyAllowsPurchasingOverDemoOrDefaultCurrentSubscription()
    {
        await using var context = fixture.CreateContext();
        var (organization, plan, _) = await this.CreatePaymentAsync(context);
        var demoPlan = await fixture.CreatePlanAsync(context, "Текущий демо", isDemo: true);
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = demoPlan.Id,
            StartDate = DateTime.UtcNow.AddDays(-5),
            EndDate = DateTime.UtcNow.AddDays(5),
            SourceCode = "demo"
        });
        await context.SaveChangesAsync();
        using var provider = this.CreateProvider(context);
        using var scope = provider.CreateScope();
        var policy = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionPurchasePolicyService>();

        Assert.True((await policy.CanPurchasePlanAsync(organization.OwnerUserId, organization.Id, plan.Id)).Success);
        await context.OrganizationSubscriptions.ExecuteDeleteAsync();
        var defaultPlan = await fixture.CreatePlanAsync(context, "Текущий базовый", isDefault: true);
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = defaultPlan.Id,
            StartDate = DateTime.UtcNow.AddDays(-5),
            EndDate = DateTime.UtcNow.AddDays(5),
            SourceCode = "manual"
        });
        await context.SaveChangesAsync();

        Assert.True((await policy.CanPurchasePlanAsync(organization.OwnerUserId, organization.Id, plan.Id)).Success);
    }

    private async Task<(Organization Organization, SubscriptionPlan Plan, SubscriptionPayment Payment)> CreatePaymentAsync(
        OpenCRMContext context,
        string operation = "purchase",
        string status = "pending",
        bool isPaid = true,
        bool isDemo = false)
    {
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, $"Бизнес-{Guid.NewGuid():N}", isDemo: isDemo);

        var operationPrefix = operation == "extension"
            ? "yookassa_workspace_extension:"
            : "yookassa_workspace_purchase:";

        var payment = await this.CreateExistingPaymentAsync(
            context, organization.OwnerUserId, plan.Id, organization.Id, operationPrefix, status, isPaid);

        return (organization, plan, payment);
    }

    private async Task<SubscriptionPayment> CreateExistingPaymentAsync(
        OpenCRMContext context,
        string userId,
        int planId,
        Guid organizationId,
        string operationPrefix,
        string status = "pending",
        bool isPaid = true)
    {
        var payment = new SubscriptionPayment
        {
            UserId = userId,
            SubscriptionPlanId = planId,
            Amount = 500,
            CreatedAt = DateTime.UtcNow,
            PaymentMethod = $"{operationPrefix}{organizationId:D}",
            PaymentStatus = status,
            PaidAt = isPaid ? DateTime.UtcNow : null,
            ExternalPaymentId = Guid.NewGuid().ToString("N")
        };

        context.SubscriptionPayments.Add(payment);
        await context.SaveChangesAsync();

        return payment;
    }

    private ServiceProvider CreateProvider(OpenCRMContext context, IOrganizationPermissionService? permissions = null)
    {
        context.ChangeTracker.Clear();

        return fixture.CreateServices(
            context,
            fixture.CreateDefaultsInitializer().Object,
            organizationPermissionService: permissions);
    }

    private static Mock<IOrganizationPermissionService> CreatePermissionService(bool canManageBilling)
    {
        var permissionService = new Mock<IOrganizationPermissionService>(MockBehavior.Strict);
        permissionService.Setup(service => service.HasPermissionAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), OrganizationPermissionCodes.BillingManage))
            .ReturnsAsync(canManageBilling);

        return permissionService;
    }
}
