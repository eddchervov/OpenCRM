using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using Npgsql;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class OrganizationSubscriptionCommandTransactionTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CommitFailureBeforeOrAfterPersistenceAppliesCommandOnce(bool extend, bool afterCommit)
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        organization.OwnerUser.CanUseTestSubscriptions = true;
        var plan = await fixture.CreatePlanAsync(seed, "Проверка фиксации");
        var endDate = DateTime.UtcNow.AddDays(3);
        endDate = new DateTime(endDate.Ticks - endDate.Ticks % 10, DateTimeKind.Utc);
        if (extend)
        {
            await fixture.AddSubscriptionAsync(seed, organization, plan, endDate);
        }

        await seed.SaveChangesAsync();
        var failure = new CommitFailureInterceptor(afterCommit);
        await using var context = fixture.CreateContext(failure);
        context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            OrganizationId = organization.Id,
            ActorUserId = organization.OwnerUserId,
            EventType = "existing",
            Title = "Прежняя запись"
        });
        await context.SaveChangesAsync();
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>();

        var result = extend
            ? await commands.ExtendSubscriptionForTestingAsync(organization.OwnerUserId, organization.Id)
            : await commands.ActivateForTestingAsync(organization.OwnerUserId, organization.Id, plan.Id);

        Assert.True(result.Success);
        Assert.Equal(1, failure.FailureCount);
        Assert.Equal(afterCommit ? 1 : 2, failure.CommitAttempts);
        await using var verification = fixture.CreateContext();
        var subscription = await verification.OrganizationSubscriptions.SingleAsync();
        Assert.Equal(2, await verification.OrganizationAuditEvents.CountAsync());
        if (extend)
        {
            Assert.Equal(endDate.AddDays(plan.DurationDays), subscription.EndDate);
        }
        else
        {
            Assert.Equal(plan.DurationDays, (subscription.EndDate - subscription.StartDate).TotalDays);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalTransactionIsPreservedAndNoSubscriptionIsChanged(bool extend)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Внешняя транзакция");
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object);
        using var scope = provider.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>();
        await using var transaction = await context.Database.BeginTransactionAsync();
        if (extend)
        {
            Assert.False((await commands.ExtendSubscriptionForTestingAsync(organization.OwnerUserId, organization.Id)).Success);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                commands.ActivateForTestingAsync(organization.OwnerUserId, organization.Id, plan.Id));
        }

        Assert.Same(transaction, context.Database.CurrentTransaction);
        await using var verification = fixture.CreateContext();
        Assert.Empty(await verification.OrganizationSubscriptions.ToListAsync());
        Assert.Empty(await verification.OrganizationAuditEvents.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentMemberRemovalRespectsTheCommandIsolationLevel(bool extend)
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        var actor = new ApplicationUser { CanUseTestSubscriptions = true };
        seed.Users.Add(actor);
        seed.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            User = actor,
            RoleCode = OrganizationRoleCodes.Owner
        });
        var plan = await fixture.CreatePlanAsync(seed, "Удаление участника");
        await seed.SaveChangesAsync();
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(actor.Id, organization.Id, OrganizationPermissionCodes.BillingManage))
            .Returns(async () =>
            {
                await using var removal = fixture.CreateContext();
                await removal.OrganizationMembers.Where(member => member.UserId == actor.Id).ExecuteDeleteAsync();
                await removal.Users.Where(user => user.Id == actor.Id).ExecuteDeleteAsync();

                return true;
            });
        await using var context = fixture.CreateContext();
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object,
            organizationPermissionService: permissions.Object);
        using var scope = provider.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>();
        if (extend)
        {
            Assert.False((await commands.ExtendSubscriptionForTestingAsync(actor.Id, organization.Id)).Success);
        }
        else
        {
            Assert.True((await commands.ActivateForTestingAsync(actor.Id, organization.Id, plan.Id)).Success);
        }

        await using var verification = fixture.CreateContext();
        Assert.Equal(extend ? 0 : 1, await verification.OrganizationSubscriptions.CountAsync());
        Assert.Equal(extend ? 0 : 1, await verification.OrganizationAuditEvents.CountAsync());
    }

    [Fact]
    public async Task MissingAuditCannotAuthorizeAutomaticRetryAfterUncertainCommit()
    {
        await using var seed = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seed);
        organization.OwnerUser.CanUseTestSubscriptions = true;
        var plan = await fixture.CreatePlanAsync(seed, "Без подтверждающего аудита");
        await seed.SaveChangesAsync();
        var failure = new CommitFailureInterceptor(afterCommit: true);
        await using var context = fixture.CreateContext(failure);
        using var provider = fixture.CreateServices(context, fixture.CreateDefaultsInitializer().Object,
            organizationAuditService: Mock.Of<IOrganizationAuditService>());
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionCommandService>()
                .ActivateForTestingAsync(organization.OwnerUserId, organization.Id, plan.Id));

        Assert.Contains("без записи аудита", exception.Message);
        Assert.Equal(1, failure.CommitAttempts);
        await using var verification = fixture.CreateContext();
        Assert.Single(await verification.OrganizationSubscriptions.ToListAsync());
    }

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

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
            if (this.FailureCount == 0)
            {
                this.FailureCount++;
                throw new PostgresException("Тестовый сбой фиксации команды.", "ERROR", "ERROR", "40001");
            }
        }
    }
}
