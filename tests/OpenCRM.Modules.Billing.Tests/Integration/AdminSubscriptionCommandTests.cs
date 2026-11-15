using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Billing.Contracts.Dtos;
using Npgsql;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class AdminSubscriptionCommandTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualGrantValidatesInputsClientPlanAndActiveSubscriptions(bool organizationScope)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await this.CreatePlanAsync(context, organizationScope);
        var commands = CreateCommands(context);
        var userId = organization.OwnerUserId;
        var organizationId = organization.Id;

        async Task<AdminBillingSubscriptionOperationDto> Grant(int days, string? reason, int? planId = null) => organizationScope
            ? await commands.GrantOrganizationPlanAsync(organizationId, planId ?? plan.Id, days, reason, userId)
            : await commands.GrantUserPlanAsync(userId, planId ?? plan.Id, days, reason, userId);

        Assert.False((await Grant(0, "Причина")).Succeeded);
        Assert.False((await Grant(367, "Причина")).Succeeded);
        Assert.False((await Grant(30, null)).Succeeded);
        Assert.False((await Grant(30, new string('а', 501))).Succeeded);
        Assert.False((await Grant(30, "Причина", int.MaxValue)).Succeeded);
        foreach (var unavailable in new[] { "inactive", "demo", "default", "wrong_type" })
        {
            plan.IsActive = unavailable != "inactive";
            plan.IsDemoPlan = unavailable == "demo";
            plan.IsDefaultPlan = unavailable == "default";
            plan.PlanTypeCode = unavailable == "wrong_type" ? (organizationScope ? "personal" : "business")
                : organizationScope ? "business" : "personal";
            await context.SaveChangesAsync();
            Assert.False((await Grant(30, "Причина")).Succeeded);
        }

        plan.IsActive = true;
        plan.IsDemoPlan = false;
        plan.IsDefaultPlan = false;
        plan.PlanTypeCode = organizationScope ? "business" : "personal";
        await context.SaveChangesAsync();
        if (organizationScope)
        {
            organizationId = Guid.NewGuid();
            Assert.True((await Grant(30, "Причина")).NotFound);
            organizationId = organization.Id;
            foreach (var state in new[] { "inactive", "archived", "deleted" })
            {
                organization.IsActive = state != "inactive";
                organization.IsArchived = state == "archived";
                organization.IsDeleted = state == "deleted";
                await context.SaveChangesAsync();
                Assert.False((await Grant(30, "Причина")).Succeeded);
            }

            organization.IsActive = true;
            organization.IsArchived = false;
            organization.IsDeleted = false;
            await context.SaveChangesAsync();
        }
        else
        {
            userId = "missing";
            Assert.True((await Grant(30, "Причина")).NotFound);
            userId = organization.OwnerUserId;
        }

        var result = await Grant(30, "  Ручная выдача  ");
        Assert.True(result.Succeeded);
        Assert.NotNull(result.EndDate);
        Assert.False((await Grant(30, "Повтор")).Succeeded);
        var audit = await context.AdminSubscriptionAudits.AsNoTracking().SingleAsync();
        Assert.Equal("granted", audit.Action);
        Assert.Equal("Ручная выдача", audit.Reason);
        Assert.Equal(organizationScope ? "organizations" : "users", audit.Scope);
        if (organizationScope)
        {
            var subscription = await context.OrganizationSubscriptions.AsNoTracking().SingleAsync();
            Assert.Equal("manual", subscription.SourceCode);
            Assert.False(subscription.AutoRenewal);
            Assert.Equal(subscription.EndDate, audit.NewEndDate);
            Assert.Equal("billing.subscription.admin_granted", (await context.OrganizationAuditEvents.SingleAsync()).EventType);
        }
        else
        {
            var subscription = await context.UserSubscriptions.AsNoTracking().SingleAsync();
            Assert.Equal("manual", subscription.SourceCode);
            Assert.False(subscription.AutoRenewal);
            Assert.Equal(subscription.EndDate, audit.NewEndDate);
            Assert.Equal("created", (await context.UserSubscriptionHistory.SingleAsync()).Status);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DemoValidatesConfigurationAndRetainsOtherSubscriptionsOnRevocation(bool organizationScope)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        if (organizationScope)
        {
            organization.PrimaryScenarioCode = OrganizationTypeCodes.Fleet;
            await context.SaveChangesAsync();
        }

        var commands = CreateCommands(context);
        var userId = organization.OwnerUserId;
        var organizationId = organization.Id;
        Task<AdminBillingSubscriptionOperationDto> Demo(bool grant, string? reason = "Демо") => organizationScope
            ? grant ? commands.GrantOrganizationDemoAsync(userId, organizationId, userId, reason)
                : commands.RevokeOrganizationDemoAsync(userId, organizationId, userId, reason)
            : grant ? commands.GrantUserDemoAsync(userId, reason, userId)
                : commands.RevokeUserDemoAsync(userId, reason, userId);

        foreach (var grant in new[] { true, false })
        {
            Assert.False((await Demo(grant, null)).Succeeded);
            Assert.False((await Demo(grant, new string('а', 501))).Succeeded);
        }

        userId = "missing";
        Assert.True((await Demo(true)).NotFound);
        Assert.True((await Demo(false)).NotFound);
        userId = organization.OwnerUserId;
        if (organizationScope)
        {
            foreach (var state in new[] { "inactive", "archived", "deleted" })
            {
                organization.IsActive = state != "inactive";
                organization.IsArchived = state == "archived";
                organization.IsDeleted = state == "deleted";
                await context.SaveChangesAsync();
                Assert.False((await Demo(true)).Succeeded);
            }

            organization.IsActive = true;
            organization.IsArchived = false;
            organization.IsDeleted = false;
            await context.SaveChangesAsync();
        }

        Assert.False((await Demo(true)).Succeeded);
        Assert.False((await Demo(false)).Succeeded);
        var demoPlan = await this.CreatePlanAsync(context, organizationScope, true);
        demoPlan.DurationDays = 0;
        await context.SaveChangesAsync();
        Assert.False((await Demo(true)).Succeeded);
        demoPlan.DurationDays = 7;
        await context.SaveChangesAsync();
        var granted = await Demo(true, "  Проверка  ");
        Assert.True(granted.Succeeded);
        Assert.False((await Demo(true)).Succeeded);
        var paidPlan = await this.CreatePlanAsync(context, organizationScope);
        if (organizationScope)
        {
            await AddSubscriptionAsync(context, organization, paidPlan, true, DateTime.UtcNow.AddDays(30));
            Assert.False((await Demo(true)).Succeeded);
        }
        var revoked = await Demo(false);
        Assert.True(revoked.Succeeded);
        if (organizationScope)
        {
            Assert.Contains("Другой действующий тариф сохранён", revoked.Message);
            Assert.Equal(2, await context.OrganizationAuditEvents.CountAsync());
            Assert.True(await context.OrganizationSubscriptions.AnyAsync(row => row.SubscriptionPlanId == paidPlan.Id && row.EndDate > DateTime.UtcNow));
        }
        else
        {
            Assert.Single(await context.UserSubscriptionHistory.ToListAsync());
            Assert.True(await context.UserSubscriptions.AllAsync(row => row.EndDate < DateTime.UtcNow));
        }

        Assert.False((await Demo(false)).Succeeded);
        var audits = await context.AdminSubscriptionAudits.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
        Assert.Equal(new[] { "demo_granted", "demo_revoked" }, audits.Select(row => row.Action));
        Assert.Equal("Проверка", audits[0].Reason);
        Assert.NotNull(audits[1].PreviousEndDate);
        Assert.True(audits[1].NewEndDate < audits[1].PreviousEndDate);
    }

    [Fact]
    public async Task OrganizationDemoRevocationWithoutAnotherPlanReportsReadOnlyWorkspace()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: OrganizationTypeCodes.Fleet);
        await this.CreatePlanAsync(context, true, true);
        var commands = CreateCommands(context);
        Assert.True((await commands.GrantOrganizationDemoAsync(organization.OwnerUserId, organization.Id, organization.OwnerUserId, "Демо")).Succeeded);
        var result = await commands.RevokeOrganizationDemoAsync(organization.OwnerUserId, organization.Id, organization.OwnerUserId, "Снять");
        Assert.True(result.Succeeded);
        Assert.Contains("режим чтения", result.Message);
    }

    [Theory]
    [InlineData(OrganizationTypeCodes.Detailing)]
    [InlineData(OrganizationTypeCodes.ServiceStation)]
    public async Task OrganizationDemoGrantIsUnavailableForFreeProScenarios(string scenario)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: scenario);
        await this.CreatePlanAsync(context, true, true);
        var commands = CreateCommands(context);

        var result = await commands.GrantOrganizationDemoAsync(
            organization.OwnerUserId, organization.Id, organization.OwnerUserId, "Проверка");

        Assert.False(result.Succeeded);
        Assert.Equal("Коммерческие тарифы не настроены: рабочее пространство работает без подписки.", result.ErrorMessage);
        Assert.Empty(await context.OrganizationSubscriptions.ToListAsync());
        Assert.Empty(await context.AdminSubscriptionAudits.ToListAsync());
    }

    [Theory]
    [InlineData(OrganizationTypeCodes.Detailing)]
    [InlineData(OrganizationTypeCodes.ServiceStation)]
    public async Task RevokingHistoricalOrganizationDemoReturnsToFree(string scenario)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: scenario);
        var demoPlan = await this.CreatePlanAsync(context, true, true);
        await AddSubscriptionAsync(context, organization, demoPlan, true, DateTime.UtcNow.AddDays(7));
        var commands = CreateCommands(context);

        var result = await commands.RevokeOrganizationDemoAsync(
            organization.OwnerUserId, organization.Id, organization.OwnerUserId, "Снять");

        Assert.True(result.Succeeded);
        Assert.Contains("продолжает работу на Free", result.Message);
        Assert.Single(await context.OrganizationSubscriptions.ToListAsync());
        Assert.Equal(new[] { "demo_revoked" },
            (await context.AdminSubscriptionAudits.AsNoTracking().OrderBy(audit => audit.Id)
                .Select(audit => audit.Action).ToListAsync()).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopAndExtendValidateVersionsAndPreserveAllActiveRows(bool organizationScope)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await this.CreatePlanAsync(context, organizationScope);
        var commands = CreateCommands(context);
        var scope = organizationScope ? " organization " : " USER ";
        var now = DateTime.UtcNow;
        var id = await AddSubscriptionAsync(context, organization, plan, organizationScope, now.AddDays(5));
        context.ChangeTracker.Clear();
        var end = organizationScope ? (await context.OrganizationSubscriptions.SingleAsync()).EndDate
            : (await context.UserSubscriptions.SingleAsync()).EndDate;
        var admin = organization.OwnerUserId;
        Task<AdminBillingSubscriptionOperationDto> Stop(string? reason = "Стоп", DateTime? expected = null, int? targetId = null, string? targetScope = null) =>
            commands.StopSubscriptionAsync(targetScope ?? scope, targetId ?? id, reason, expected, admin);
        Task<AdminBillingSubscriptionOperationDto> Extend(int days = 1, string? reason = "Продлить", DateTime? expected = null, int? targetId = null, string? targetScope = null) =>
            commands.ExtendSubscriptionAsync(targetScope ?? scope, targetId ?? id, days, reason, expected, admin);

        Assert.False((await Stop(null, end)).Succeeded);
        Assert.False((await Stop(new string('а', 501), end)).Succeeded);
        Assert.False((await Stop()).Succeeded);
        Assert.False((await Stop(expected: end, targetScope: "unknown")).Succeeded);
        Assert.True((await Stop(expected: end, targetId: int.MaxValue)).NotFound);
        Assert.False((await Stop(expected: end.AddDays(1))).Succeeded);
        Assert.False((await Extend(0, expected: end)).Succeeded);
        Assert.False((await Extend(367, expected: end)).Succeeded);
        Assert.False((await Extend(reason: null, expected: end)).Succeeded);
        Assert.False((await Extend(reason: new string('а', 501), expected: end)).Succeeded);
        Assert.False((await Extend()).Succeeded);
        Assert.False((await Extend(expected: end, targetScope: "unknown")).Succeeded);
        Assert.True((await Extend(expected: end, targetId: int.MaxValue)).NotFound);
        Assert.False((await Extend(expected: end.AddDays(1))).Succeeded);
        var otherId = 0;
        if (organizationScope)
        {
            otherId = await AddSubscriptionAsync(context, organization, plan, true, now.AddDays(10));
            Assert.False((await Extend(expected: end)).Succeeded);
        }
        var stopped = await Stop(expected: end);
        Assert.True(stopped.Succeeded);
        Assert.False((await Stop(expected: end)).Succeeded);
        var stoppedRows = organizationScope
            ? await context.OrganizationSubscriptions.AsNoTracking().Select(row => new { row.EndDate, row.AutoRenewal }).ToListAsync()
            : await context.UserSubscriptions.AsNoTracking().Select(row => new { row.EndDate, row.AutoRenewal }).ToListAsync();
        Assert.Equal(organizationScope ? 2 : 1, stoppedRows.Count);
        Assert.All(stoppedRows, row => { Assert.False(row.AutoRenewal); Assert.True(row.EndDate < DateTime.UtcNow); });
        Assert.Equal(organizationScope ? 2 : 1, await context.AdminSubscriptionAudits.CountAsync(row => row.Action == "stopped"));
        context.ChangeTracker.Clear();
        var expiredEnd = organizationScope ? (await context.OrganizationSubscriptions.SingleAsync(row => row.Id == id)).EndDate
            : (await context.UserSubscriptions.SingleAsync(row => row.Id == id)).EndDate;
        var restarted = await Extend(2, expected: expiredEnd);
        Assert.True(restarted.Succeeded);
        Assert.InRange(restarted.EndDate!.Value, DateTime.UtcNow.AddDays(2).AddSeconds(-10), DateTime.UtcNow.AddDays(2));
        context.ChangeTracker.Clear();
        var currentEnd = organizationScope ? (await context.OrganizationSubscriptions.SingleAsync(row => row.Id == id)).EndDate
            : (await context.UserSubscriptions.SingleAsync(row => row.Id == id)).EndDate;
        var extended = await Extend(1, expected: currentEnd, targetScope: organizationScope ? "organizations" : "users");
        Assert.True(extended.Succeeded);
        Assert.Equal(currentEnd.AddDays(1), extended.EndDate);
        var audit = await context.AdminSubscriptionAudits.AsNoTracking().OrderByDescending(row => row.Id).FirstAsync();
        Assert.Equal("extended", audit.Action);
        Assert.Equal(currentEnd, audit.PreviousEndDate);
        Assert.Equal(currentEnd.AddDays(1), audit.NewEndDate);
        Assert.NotEqual(id, otherId);
    }

    [Fact]
    public async Task RetryRollsBackPartialGrantAndRebuildsTrackerBeforeCommitting()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        var plan = await this.CreatePlanAsync(seed, false);
        var failure = new FailAuditOnce();
        await using var context = fixture.CreateContext(failure);
        var commands = CreateCommands(context);

        var result = await commands.GrantUserPlanAsync(organization.OwnerUserId, plan.Id, 30, "Повтор БД", organization.OwnerUserId);

        Assert.True(result.Succeeded);
        Assert.True(failure.HasFailed);
        Assert.Equal(1, await context.UserSubscriptions.CountAsync());
        Assert.Equal(1, await context.UserSubscriptionHistory.CountAsync());
        Assert.Equal(1, await context.AdminSubscriptionAudits.CountAsync());
    }

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<SubscriptionPlan> CreatePlanAsync(OpenCRMContext context, bool organizationScope, bool demo = false)
    {
        var plan = await fixture.CreatePlanAsync(context, $"Тариф {Guid.NewGuid():N}", isDemo: demo);
        plan.PlanTypeCode = organizationScope ? "business" : "personal";
        await context.SaveChangesAsync();

        return plan;
    }

    private static IAdminSubscriptionCommandService CreateCommands(OpenCRMContext context)
    {
        var mutation = new AdminSubscriptionMutation(context);

        return new AdminSubscriptionCommandService(new AdminPersonalSubscriptionCommandService(context, mutation),
            new AdminOrganizationSubscriptionCommandService(context, mutation), new AdminSubscriptionAdjustmentService(context, mutation));
    }

    private static async Task<int> AddSubscriptionAsync(
        OpenCRMContext context, Organization organization, SubscriptionPlan plan, bool organizationScope, DateTime end)
    {
        if (organizationScope)
        {
            var subscription = new OrganizationSubscription
            {
                OrganizationId = organization.Id, SubscriptionPlanId = plan.Id,
                StartDate = end.AddDays(-30), EndDate = end, AutoRenewal = true
            };
            context.OrganizationSubscriptions.Add(subscription);
            await context.SaveChangesAsync();

            return subscription.Id;
        }

        var personal = new UserSubscription
        {
            UserId = organization.OwnerUserId, SubscriptionPlanId = plan.Id,
            StartDate = end.AddDays(-30), EndDate = end, AutoRenewal = true
        };
        context.UserSubscriptions.Add(personal);
        await context.SaveChangesAsync();

        return personal.Id;
    }

    private sealed class FailAuditOnce : SaveChangesInterceptor
    {
        public bool HasFailed { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!this.HasFailed && eventData.Context!.ChangeTracker.Entries<AdminSubscriptionAudit>()
                .Any(entry => entry.State == EntityState.Added))
            {
                this.HasFailed = true;
                throw new PostgresException("Тестовый конфликт сериализации.", "ERROR", "ERROR", "40001");
            }

            return new ValueTask<InterceptionResult<int>>(result);
        }
    }
}
