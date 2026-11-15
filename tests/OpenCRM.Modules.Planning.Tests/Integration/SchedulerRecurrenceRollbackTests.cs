using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Modules.Planning.Contracts.Dtos;

namespace OpenCRM.Modules.Planning.Tests.Integration;

[Collection(PlanningDatabaseCollection.Name)]
public sealed class SchedulerRecurrenceRollbackTests(PlanningDatabaseFixture fixture)
{
    [Fact]
    public async Task CreateRollsBackTemplateAndAuditWhenRecurringInstanceInsertFails()
    {
        var failure = new SchedulerTaskInsertFailureInterceptor(failOnInsert: 2);
        await using var context = fixture.CreateContext(failure);
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<ISchedulerTaskCommandService>();
        var start = DateTime.UtcNow.Date.AddDays(1).AddHours(9);
        failure.Arm();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => commands.CreateTaskAsync(
            organization.OwnerUserId, organization.Id, new CreateOrganizationSchedulerTaskDto
            {
                Title = "Шаблон должен откатиться",
                StartAt = start,
                RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Daily,
                RecurrenceInterval = 1,
                RecurrenceUntil = start.AddDays(3)
            }));

        Assert.Equal("Тестовый сбой вставки экземпляра повторяющейся задачи.", exception.InnerException!.Message);
        Assert.Equal(2, failure.TaskInsertAttempts);
        await using var verificationContext = fixture.CreateContext();
        Assert.False(await verificationContext.OrganizationSchedulerTasks.AsNoTracking()
            .AnyAsync(value => value.OrganizationId == organization.Id));
        Assert.False(await verificationContext.OrganizationAuditEvents.AsNoTracking()
            .AnyAsync(value => value.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task UpdateRollsBackTemplateAndAuditWhenRefreshInsertFails()
    {
        var failure = new SchedulerTaskInsertFailureInterceptor(failOnInsert: 1);
        await using var context = fixture.CreateContext(failure);
        var organization = await fixture.CreateOrganizationAsync(context);
        var task = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Исходная задача");
        context.OrganizationSchedulerTasks.Add(task);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<ISchedulerTaskCommandService>();
        var start = DateTime.UtcNow.Date.AddDays(1).AddHours(9);
        failure.Arm();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => commands.UpdateManualTaskAsync(
            organization.OwnerUserId, organization.Id, task.Id, new UpdateOrganizationSchedulerTaskDto
            {
                Title = "Изменение должно откатиться",
                StartAt = start,
                RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Daily,
                RecurrenceInterval = 1,
                RecurrenceUntil = start.AddDays(3)
            }));

        Assert.Equal("Тестовый сбой вставки экземпляра повторяющейся задачи.", exception.InnerException!.Message);
        Assert.Equal(1, failure.TaskInsertAttempts);
        await using var verificationContext = fixture.CreateContext();
        var persistedTask = await verificationContext.OrganizationSchedulerTasks.AsNoTracking().SingleAsync(value => value.Id == task.Id);
        Assert.Equal("Исходная задача", persistedTask.Title);
        Assert.Equal(OrganizationSchedulerTaskRecurrenceCodes.None, persistedTask.RecurrenceCode);
        Assert.Empty(await verificationContext.OrganizationAuditEvents.AsNoTracking()
            .Where(value => value.OrganizationId == organization.Id)
            .ToListAsync());
    }

    private sealed class SchedulerTaskInsertFailureInterceptor(int failOnInsert) : DbCommandInterceptor
    {
        private bool _isArmed;

        internal int TaskInsertAttempts { get; private set; }

        internal void Arm() => this._isArmed = true;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (this._isArmed && command.CommandText.Contains("INSERT INTO \"OrganizationSchedulerTasks\"", StringComparison.Ordinal))
            {
                this.TaskInsertAttempts++;
                if (this.TaskInsertAttempts == failOnInsert)
                {
                    throw new InvalidOperationException("Тестовый сбой вставки экземпляра повторяющейся задачи.");
                }
            }

            return ValueTask.FromResult(result);
        }
    }
}
