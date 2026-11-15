using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Modules.Planning.Contracts.Dtos;
using Npgsql;

namespace OpenCRM.Modules.Planning.Tests.Integration;

[Collection(PlanningDatabaseCollection.Name)]
public sealed class SchedulerTransactionTests(PlanningDatabaseFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitRetryKeepsExactlyOneTaskAndAudit(bool failAfterCommit)
    {
        var failure = new PlanningCommitFailureInterceptor(failAfterCommit);
        await using var context = fixture.CreateContext(failure);
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<ISchedulerTaskCommandService>();
        failure.Arm();

        var taskId = await commands.CreateTaskAsync(organization.OwnerUserId, organization.Id,
            new CreateOrganizationSchedulerTaskDto { Title = "Проверка повтора фиксации" });

        Assert.Equal(1, failure.Failures);
        Assert.Equal(failAfterCommit ? 1 : 2, failure.CommitAttempts);
        await using var verificationContext = fixture.CreateContext();
        var savedTask = await verificationContext.OrganizationSchedulerTasks.AsNoTracking().SingleAsync(value => value.Id == taskId);
        var auditEvents = await verificationContext.OrganizationAuditEvents.AsNoTracking()
            .Where(value => value.OrganizationId == organization.Id)
            .ToListAsync();
        Assert.Equal("Проверка повтора фиксации", savedTask.Title);
        Assert.Single(auditEvents);
        Assert.Equal("scheduler.task.created", auditEvents[0].EventType);
        Assert.Equal(taskId.ToString("D"), auditEvents[0].TargetId);
    }

    [Fact]
    public async Task AmbientTransactionControlsManualTaskCommitAndRollback()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        Guid taskId = Guid.Empty;

        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            using var services = fixture.CreateServices(context);
            using var scope = services.CreateScope();
            var commands = scope.ServiceProvider.GetRequiredService<ISchedulerTaskCommandService>();
            taskId = await commands.CreateTaskAsync(organization.OwnerUserId, organization.Id,
                new CreateOrganizationSchedulerTaskDto { Title = "Откат внешней транзакцией" });

            Assert.NotNull(context.Database.CurrentTransaction);
            await transaction.RollbackAsync();
        });

        await using var verificationContext = fixture.CreateContext();
        Assert.False(await verificationContext.OrganizationSchedulerTasks.AsNoTracking().AnyAsync(value => value.Id == taskId));
        Assert.False(await verificationContext.OrganizationAuditEvents.AsNoTracking().AnyAsync(value => value.OrganizationId == organization.Id));
    }

    private sealed class PlanningCommitFailureInterceptor(bool failAfterCommit) : DbTransactionInterceptor
    {
        private bool _isArmed;
        private bool _hasFailed;

        internal int CommitAttempts { get; private set; }
        internal int Failures { get; private set; }

        internal void Arm() => this._isArmed = true;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (this._isArmed)
            {
                this.CommitAttempts++;
                if (!failAfterCommit)
                {
                    this.FailOnce();
                }
            }

            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (this._isArmed && failAfterCommit && !this._hasFailed)
            {
                this.FailOnce();
            }

            return Task.CompletedTask;
        }

        private void FailOnce()
        {
            if (this._hasFailed)
            {
                return;
            }

            this._hasFailed = true;
            this.Failures++;
            throw new PostgresException("Тестовый сбой фиксации планировщика.", "ERROR", "ERROR", "40001");
        }
    }
}
