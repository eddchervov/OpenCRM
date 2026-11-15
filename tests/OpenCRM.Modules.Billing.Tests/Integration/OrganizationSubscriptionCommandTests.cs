using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class OrganizationSubscriptionCommandTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    private readonly BillingDatabaseFixture _fixture = fixture;

    public Task InitializeAsync() => this._fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DemoActivationUsesRequestedDurationAndWritesAudit()
    {
        await using var context = this._fixture.CreateContext();
        var organization = await this._fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        var plan = await this._fixture.CreatePlanAsync(context, "Демо-командный", isDemo: true);
        var defaults = this._fixture.CreateDefaultsInitializer();
        using var provider = this._fixture.CreateServices(context, defaults.Object);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>()
            .ActivateDemoAsync(organization.OwnerUserId, organization.Id, plan.Id, durationDays: 11);

        Assert.True(result.Success);
        defaults.Verify(service => service.EnsureDefaultsAsync(), Times.AtLeastOnce);
        var subscription = await context.OrganizationSubscriptions.AsNoTracking().SingleAsync();
        Assert.Equal(plan.Id, subscription.SubscriptionPlanId);
        Assert.Equal("demo", subscription.SourceCode);
        Assert.InRange((subscription.EndDate - subscription.StartDate).TotalDays, 10.99, 11.01);
        var audit = await context.OrganizationAuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal("billing.subscription.activated_demo", audit.EventType);
        Assert.Equal(organization.OwnerUserId, audit.ActorUserId);
        Assert.Equal(plan.Id.ToString(), audit.TargetId);
    }

    [Fact]
    public async Task DemoActivationUsesPlanDurationWhenNoOverrideIsProvided()
    {
        await using var context = this._fixture.CreateContext();
        var organization = await this._fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        var plan = await this._fixture.CreatePlanAsync(context, "Демо по умолчанию", isDemo: true);
        using var provider = this._fixture.CreateServices(context, this._fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>()
            .ActivateDemoAsync(organization.OwnerUserId, organization.Id, plan.Id);

        Assert.True(result.Success);
        var subscription = await context.OrganizationSubscriptions.AsNoTracking().SingleAsync();
        Assert.Equal(plan.DurationDays, (subscription.EndDate - subscription.StartDate).TotalDays, precision: 5);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("not-demo")]
    [InlineData("default")]
    [InlineData("personal")]
    public async Task DemoActivationRejectsUnavailablePlan(string state)
    {
        await using var context = this._fixture.CreateContext();
        var organization = await this._fixture.CreateOrganizationAsync(context);
        var plan = await this._fixture.CreatePlanAsync(context, "Проверка демо", isDemo: state is not ("not-demo" or "default"));
        if (state == "inactive")
        {
            plan.IsActive = false;
        }
        else if (state == "default")
        {
            plan.IsDefaultPlan = true;
        }
        else if (state == "personal")
        {
            plan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        }

        await context.SaveChangesAsync();
        using var provider = this._fixture.CreateServices(context, this._fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var planId = state == "missing" ? int.MaxValue : plan.Id;

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>()
            .ActivateDemoAsync(organization.OwnerUserId, organization.Id, planId);

        Assert.False(result.Success);
        Assert.Empty(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
        Assert.Empty(await context.OrganizationAuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task DemoActivationRejectsWorkspaceWithSubscriptionHistory()
    {
        await using var context = this._fixture.CreateContext();
        var organization = await this._fixture.CreateOrganizationAsync(context);
        var demoPlan = await this._fixture.CreatePlanAsync(context, "Новое демо", isDemo: true);
        var previousPlan = await this._fixture.CreatePlanAsync(context, "Предыдущий платный");
        await this._fixture.AddSubscriptionAsync(context, organization, previousPlan, DateTime.UtcNow.AddDays(-1));
        using var provider = this._fixture.CreateServices(context, this._fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>()
            .ActivateDemoAsync(organization.OwnerUserId, organization.Id, demoPlan.Id);

        Assert.False(result.Success);
        Assert.Single(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
        Assert.Empty(await context.OrganizationAuditEvents.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("archived")]
    [InlineData("not-member")]
    [InlineData("denied")]
    public async Task CommandsRequireAvailableWorkspaceMembershipAndBillingPermission(string state)
    {
        await using var context = this._fixture.CreateContext();
        var organization = await this._fixture.CreateOrganizationAsync(context);
        var plan = await this._fixture.CreatePlanAsync(context, "Тариф команды", isDemo: true);
        var userId = organization.OwnerUserId;
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), OrganizationPermissionCodes.BillingManage))
            .ReturnsAsync(state != "denied");
        if (state == "inactive")
        {
            organization.IsActive = false;
        }
        else if (state == "deleted")
        {
            organization.IsDeleted = true;
        }
        else if (state == "archived")
        {
            organization.IsArchived = true;
        }
        else if (state == "not-member")
        {
            var otherUser = new ApplicationUser { Email = $"not-member-{Guid.NewGuid():N}@example.test" };
            context.Users.Add(otherUser);
            await context.SaveChangesAsync();
            userId = otherUser.Id;
        }

        await context.SaveChangesAsync();
        using var provider = this._fixture.CreateServices(
            context,
            this._fixture.CreateDefaultsInitializer().Object,
            organizationPermissionService: permissions.Object);
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>()
                .ActivateDemoAsync(userId, organization.Id, plan.Id));

        Assert.Empty(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
        Assert.Empty(await context.OrganizationAuditEvents.AsNoTracking().ToListAsync());
        if (state == "denied")
        {
            permissions.Verify(service => service.HasPermissionAsync(
                userId, organization.Id, OrganizationPermissionCodes.BillingManage), Times.Once);
        }
    }

    [Fact]
    public async Task TestingActivationRequiresAccountEligibilityAndRejectsUnavailablePlan()
    {
        await using var context = this._fixture.CreateContext();
        var organization = await this._fixture.CreateOrganizationAsync(context);
        var inactivePlan = await this._fixture.CreatePlanAsync(context, "Неактивный тестовый");
        inactivePlan.IsActive = false;
        var personalPlan = await this._fixture.CreatePlanAsync(context, "Личный тестовый");
        personalPlan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal;
        var defaultPlan = await this._fixture.CreatePlanAsync(context, "Базовый тестовый", isDefault: true);
        var demoPlan = await this._fixture.CreatePlanAsync(context, "Демо тестовый", isDemo: true);
        var validPlan = await this._fixture.CreatePlanAsync(context, "Рабочий тестовый");
        await context.SaveChangesAsync();
        using var provider = this._fixture.CreateServices(context, this._fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>();

        var denied = await command.ActivateForTestingAsync(organization.OwnerUserId, organization.Id, inactivePlan.Id);
        var personal = await command.ActivateForTestingAsync(organization.OwnerUserId, organization.Id, personalPlan.Id);
        var defaultResult = await command.ActivateForTestingAsync(organization.OwnerUserId, organization.Id, defaultPlan.Id);
        var ineligible = await command.ActivateForTestingAsync(organization.OwnerUserId, organization.Id, validPlan.Id);
        var missingUser = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            command.ActivateForTestingAsync("missing-user", organization.Id, demoPlan.Id));

        Assert.False(denied.Success);
        Assert.False(personal.Success);
        Assert.False(defaultResult.Success);
        Assert.False(ineligible.Success);
        Assert.Equal("Организация не найдена или доступ закрыт.", missingUser.Message);
        Assert.Empty(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("demo")]
    [InlineData("default")]
    public async Task TestingActivationCanReplaceDemoOrDefaultSubscriptionAndWritesAudit(string currentPlanKind)
    {
        await using var context = this._fixture.CreateContext();
        var organization = await this._fixture.CreateOrganizationAsync(context);
        var user = await context.Users.SingleAsync(item => item.Id == organization.OwnerUserId);
        user.CanUseTestSubscriptions = true;
        var oldPlan = await this._fixture.CreatePlanAsync(
            context,
            "Тариф для замены",
            isDemo: currentPlanKind == "demo",
            isDefault: currentPlanKind == "default");
        var newPlan = await this._fixture.CreatePlanAsync(context, "Тестовый рабочий тариф");
        await context.SaveChangesAsync();
        var oldEndDate = DateTime.UtcNow.AddDays(4);
        await this._fixture.AddSubscriptionAsync(context, organization, oldPlan, oldEndDate);
        using var provider = this._fixture.CreateServices(context, this._fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>()
            .ActivateForTestingAsync(organization.OwnerUserId, organization.Id, newPlan.Id);

        Assert.True(result.Success);
        var subscriptions = await context.OrganizationSubscriptions.AsNoTracking()
            .OrderBy(item => item.Id)
            .ToListAsync();
        Assert.Equal(2, subscriptions.Count);
        Assert.Equal(newPlan.Id, subscriptions[1].SubscriptionPlanId);
        Assert.Equal("manual", subscriptions[1].SourceCode);
        Assert.True(subscriptions[0].EndDate < oldEndDate);
        var audit = await context.OrganizationAuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal("billing.subscription.activated_test", audit.EventType);
        Assert.Equal(newPlan.Id.ToString(), audit.TargetId);
    }

    [Fact]
    public async Task TestingActivationCannotReplaceCurrentPaidSubscription()
    {
        await using var context = this._fixture.CreateContext();
        var organization = await this._fixture.CreateOrganizationAsync(context);
        var user = await context.Users.SingleAsync(item => item.Id == organization.OwnerUserId);
        user.CanUseTestSubscriptions = true;
        var currentPlan = await this._fixture.CreatePlanAsync(context, "Текущий оплаченный");
        var requestedPlan = await this._fixture.CreatePlanAsync(context, "Новый тестовый");
        await context.SaveChangesAsync();
        var currentEndDate = DateTime.UtcNow.AddDays(10);
        currentEndDate = new DateTime(currentEndDate.Ticks - currentEndDate.Ticks % 10, DateTimeKind.Utc);
        await this._fixture.AddSubscriptionAsync(context, organization, currentPlan, currentEndDate);
        using var provider = this._fixture.CreateServices(context, this._fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>()
            .ActivateForTestingAsync(organization.OwnerUserId, organization.Id, requestedPlan.Id);

        Assert.False(result.Success);
        Assert.Single(await context.OrganizationSubscriptions.AsNoTracking().ToListAsync());
        Assert.Equal(currentEndDate, (await context.OrganizationSubscriptions.AsNoTracking().SingleAsync()).EndDate);
        Assert.Empty(await context.OrganizationAuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task TestingActivationSaveFailureDoesNotPersistSubscriptionOrAudit()
    {
        await using var seed = this._fixture.CreateContext();
        var organization = await this._fixture.CreateOrganizationAsync(seed);
        var user = await seed.Users.SingleAsync(item => item.Id == organization.OwnerUserId);
        user.CanUseTestSubscriptions = true;
        var plan = await this._fixture.CreatePlanAsync(seed, "Сбой сохранения тестовой активации");
        await seed.SaveChangesAsync();
        var failure = new SaveFailureInterceptor();
        await using var context = this._fixture.CreateContext(failure);
        using var provider = this._fixture.CreateServices(context, this._fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>()
                .ActivateForTestingAsync(organization.OwnerUserId, organization.Id, plan.Id));

        await using var verification = this._fixture.CreateContext();
        Assert.Empty(await verification.OrganizationSubscriptions.ToListAsync());
        Assert.Empty(await verification.OrganizationAuditEvents.ToListAsync());
    }

    [Fact]
    public async Task TestingExtensionRejectsMissingAndDemoSubscriptions()
    {
        await using var context = this._fixture.CreateContext();
        var organization = await this._fixture.CreateOrganizationAsync(context);
        var user = await context.Users.SingleAsync(item => item.Id == organization.OwnerUserId);
        user.CanUseTestSubscriptions = true;
        var demoPlan = await this._fixture.CreatePlanAsync(context, "Демо для отказа", isDemo: true);
        await context.SaveChangesAsync();
        using var provider = this._fixture.CreateServices(context, this._fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>();

        var missing = await command.ExtendSubscriptionForTestingAsync(organization.OwnerUserId, organization.Id);
        var demoEndDate = DateTime.UtcNow.AddDays(3);
        demoEndDate = new DateTime(demoEndDate.Ticks - demoEndDate.Ticks % 10, DateTimeKind.Utc);
        await this._fixture.AddSubscriptionAsync(context, organization, demoPlan, demoEndDate);
        var missingUser = await command.ExtendSubscriptionForTestingAsync("missing-user", organization.Id);
        var demo = await command.ExtendSubscriptionForTestingAsync(organization.OwnerUserId, organization.Id);

        Assert.False(missing.Success);
        Assert.False(missingUser.Success);
        Assert.False(demo.Success);
        Assert.Empty(await context.OrganizationAuditEvents.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("ineligible")]
    [InlineData("default")]
    [InlineData("too-early")]
    public async Task TestingExtensionRejectsIneligibleAccountOrSubscription(string state)
    {
        await using var context = this._fixture.CreateContext();
        var organization = await this._fixture.CreateOrganizationAsync(context);
        var plan = await this._fixture.CreatePlanAsync(context, "Тариф для проверки", isDefault: state == "default");
        if (state != "ineligible")
        {
            var user = await context.Users.SingleAsync(item => item.Id == organization.OwnerUserId);
            user.CanUseTestSubscriptions = true;
        }

        await context.SaveChangesAsync();
        if (state != "ineligible")
        {
            var endDate = DateTime.UtcNow.AddDays(state == "too-early" ? 20 : 3);
            await this._fixture.AddSubscriptionAsync(context, organization, plan, endDate);
        }

        using var provider = this._fixture.CreateServices(context, this._fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>()
            .ExtendSubscriptionForTestingAsync(organization.OwnerUserId, organization.Id);

        Assert.False(result.Success);
        Assert.Empty(await context.OrganizationAuditEvents.AsNoTracking().ToListAsync());
        if (state == "too-early")
        {
            Assert.Contains("Продление откроется за 7 дней", result.Error);
        }
    }

    [Fact]
    public async Task TestingExtensionAddsPlanDurationAndAudit()
    {
        await using var context = this._fixture.CreateContext();
        var organization = await this._fixture.CreateOrganizationAsync(context);
        var user = await context.Users.SingleAsync(item => item.Id == organization.OwnerUserId);
        user.CanUseTestSubscriptions = true;
        var plan = await this._fixture.CreatePlanAsync(context, "Продление тестом");
        await context.SaveChangesAsync();
        var oldEndDate = DateTime.UtcNow.AddDays(3);
        oldEndDate = new DateTime(oldEndDate.Ticks - oldEndDate.Ticks % 10, DateTimeKind.Utc);
        await this._fixture.AddSubscriptionAsync(context, organization, plan, oldEndDate);
        using var provider = this._fixture.CreateServices(context, this._fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>()
            .ExtendSubscriptionForTestingAsync(organization.OwnerUserId, organization.Id);

        Assert.True(result.Success);
        var subscription = await context.OrganizationSubscriptions.AsNoTracking().SingleAsync();
        Assert.Equal(oldEndDate.AddDays(plan.DurationDays), subscription.EndDate);
        var audit = await context.OrganizationAuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal("billing.subscription.extended_test", audit.EventType);
        Assert.Equal(plan.Id.ToString(), audit.TargetId);
    }

    [Fact]
    public async Task TestingExtensionAuditFailureRollsBackDateChange()
    {
        await using var seed = this._fixture.CreateContext();
        var organization = await this._fixture.CreateOrganizationAsync(seed);
        var user = await seed.Users.SingleAsync(item => item.Id == organization.OwnerUserId);
        user.CanUseTestSubscriptions = true;
        var plan = await this._fixture.CreatePlanAsync(seed, "Сбой аудита продления");
        await seed.SaveChangesAsync();
        var oldEndDate = DateTime.UtcNow.AddDays(3);
        oldEndDate = new DateTime(oldEndDate.Ticks - oldEndDate.Ticks % 10, DateTimeKind.Utc);
        await this._fixture.AddSubscriptionAsync(seed, organization, plan, oldEndDate);
        var audit = new Mock<IOrganizationAuditService>();
        audit.Setup(service => service.AddAsync(It.IsAny<OrganizationAuditEventCreateDto>(), false))
            .ThrowsAsync(new InvalidOperationException("Сбой аудита."));
        await using var context = this._fixture.CreateContext();
        using var provider = this._fixture.CreateServices(
            context,
            this._fixture.CreateDefaultsInitializer().Object,
            organizationAuditService: audit.Object);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>()
            .ExtendSubscriptionForTestingAsync(organization.OwnerUserId, organization.Id);

        Assert.False(result.Success);
        await using var verification = this._fixture.CreateContext();
        Assert.Equal(oldEndDate, (await verification.OrganizationSubscriptions.SingleAsync()).EndDate);
        Assert.Empty(await verification.OrganizationAuditEvents.ToListAsync());
        audit.VerifyAll();
    }

    [Fact]
    public async Task TestingExtensionSaveFailureRollsBackDateChangeAndAudit()
    {
        await using var seed = this._fixture.CreateContext();
        var organization = await this._fixture.CreateOrganizationAsync(seed);
        var user = await seed.Users.SingleAsync(item => item.Id == organization.OwnerUserId);
        user.CanUseTestSubscriptions = true;
        var plan = await this._fixture.CreatePlanAsync(seed, "Сбой сохранения продления");
        await seed.SaveChangesAsync();
        var oldEndDate = DateTime.UtcNow.AddDays(3);
        oldEndDate = new DateTime(oldEndDate.Ticks - oldEndDate.Ticks % 10, DateTimeKind.Utc);
        await this._fixture.AddSubscriptionAsync(seed, organization, plan, oldEndDate);
        var failure = new SaveFailureInterceptor();
        await using var context = this._fixture.CreateContext(failure);
        using var provider = this._fixture.CreateServices(context, this._fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>()
            .ExtendSubscriptionForTestingAsync(organization.OwnerUserId, organization.Id);

        Assert.False(result.Success);
        await using var verification = this._fixture.CreateContext();
        Assert.Equal(oldEndDate, (await verification.OrganizationSubscriptions.SingleAsync()).EndDate);
        Assert.Empty(await verification.OrganizationAuditEvents.ToListAsync());
    }

    private sealed class SaveFailureInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            throw new DbUpdateException("Сохранение намеренно отклонено тестом.");
        }
    }
}
