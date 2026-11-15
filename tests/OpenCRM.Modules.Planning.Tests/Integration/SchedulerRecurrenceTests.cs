using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Planning.Contracts;
using Npgsql;

namespace OpenCRM.Modules.Planning.Tests.Integration;

[Collection(PlanningDatabaseCollection.Name)]
public sealed class SchedulerRecurrenceTests(PlanningDatabaseFixture fixture)
{
    [Theory]
    [InlineData(OrganizationSchedulerTaskRecurrenceCodes.Daily, 2, 2)]
    [InlineData(OrganizationSchedulerTaskRecurrenceCodes.Weekly, 1, 2)]
    [InlineData(OrganizationSchedulerTaskRecurrenceCodes.Monthly, 1, 3)]
    public async Task GenerateCreatesExpectedDatesWithinHorizonAndIsIdempotent(string recurrenceCode, int interval, int expectedCount)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var start = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var template = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Регламентная задача", start, start.AddHours(-1));
        template.EndAt = start.AddHours(2);
        template.RecurrenceCode = recurrenceCode;
        template.RecurrenceInterval = interval;
        context.OrganizationSchedulerTasks.Add(template);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var recurrence = scope.ServiceProvider.GetRequiredService<ISchedulerRecurrenceService>();
        var horizon = recurrenceCode switch
        {
            OrganizationSchedulerTaskRecurrenceCodes.Daily => new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            OrganizationSchedulerTaskRecurrenceCodes.Weekly => new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            _ => new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var created = await recurrence.GenerateRecurringTaskInstancesAsync(organization.OwnerUserId, organization.Id, horizon);
        var createdAgain = await recurrence.GenerateRecurringTaskInstancesAsync(organization.OwnerUserId, organization.Id, horizon);
        var children = await context.OrganizationSchedulerTasks.AsNoTracking()
            .Where(value => value.RecurrenceParentTaskId == template.Id && !value.IsDeleted)
            .OrderBy(value => value.StartAt)
            .ToListAsync();

        Assert.Equal(expectedCount, created);
        Assert.Equal(0, createdAgain);
        Assert.Equal(1, await context.OrganizationAuditEvents.CountAsync(value =>
            value.OrganizationId == organization.Id && value.EventType == "scheduler.recurrence.generated"));
        Assert.Equal(expectedCount, children.Count);
        Assert.All(children, child =>
        {
            Assert.Equal(TimeSpan.FromHours(2), child.EndAt!.Value - child.StartAt!.Value);
            Assert.Equal(child.StartAt!.Value.AddHours(-1), child.ReminderAt);
            Assert.Equal(OrganizationSchedulerTaskRecurrenceCodes.None, child.RecurrenceCode);
            Assert.Equal(template.Id, child.RecurrenceParentTaskId);
        });
    }

    [Fact]
    public async Task GenerationUsesLatestTemplateValuesWhenScopedContextHasStaleTrackedEntity()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var start = DateTime.UtcNow.Date.AddDays(-1).AddHours(9);
        var template = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Старое название", start);
        template.RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Daily;
        template.RecurrenceInterval = 1;
        context.OrganizationSchedulerTasks.Add(template);
        await context.SaveChangesAsync();
        var staleTemplate = await context.OrganizationSchedulerTasks.SingleAsync(value => value.Id == template.Id);

        await using (var updateContext = fixture.CreateContext())
        {
            var persistedTemplate = await updateContext.OrganizationSchedulerTasks.SingleAsync(value => value.Id == template.Id);
            persistedTemplate.Title = "Актуальное название";
            await updateContext.SaveChangesAsync();
        }

        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var recurrence = scope.ServiceProvider.GetRequiredService<ISchedulerRecurrenceService>();
        var created = await recurrence.GenerateRecurringTaskInstancesAsync(
            organization.OwnerUserId, organization.Id, DateTime.UtcNow.Date.AddDays(2));
        var generatedChildren = await context.OrganizationSchedulerTasks.AsNoTracking()
            .Where(value => value.RecurrenceParentTaskId == staleTemplate.Id)
            .ToListAsync();

        Assert.Equal(3, created);
        Assert.Equal("Старое название", staleTemplate.Title);
        Assert.Equal(3, generatedChildren.Count);
        Assert.All(generatedChildren, value => Assert.Equal("Актуальное название", value.Title));
    }

    [Fact]
    public async Task UpdatingTemplateRefreshesPristineChildrenAndPreservesEditedChild()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var start = DateTime.UtcNow.Date.AddDays(1).AddHours(9);
        var template = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Старое название", start);
        template.RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Daily;
        template.RecurrenceInterval = 1;
        template.RecurrenceUntil = start.AddDays(5);
        context.OrganizationSchedulerTasks.Add(template);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var recurrence = scope.ServiceProvider.GetRequiredService<ISchedulerRecurrenceService>();
        var commands = scope.ServiceProvider.GetRequiredService<ISchedulerTaskCommandService>();
        await recurrence.GenerateRecurringTaskInstancesAsync(organization.OwnerUserId, organization.Id, start.AddDays(5));
        var child = await context.OrganizationSchedulerTasks
            .Where(value => value.RecurrenceParentTaskId == template.Id && !value.IsDeleted)
            .OrderBy(value => value.StartAt)
            .FirstAsync();
        child.Title = "Изменено вручную";
        child.StatusCode = OrganizationSchedulerTaskStatusCodes.InProgress;
        child.UpdatedAt = child.CreatedAt.AddMinutes(1);
        await context.SaveChangesAsync();

        await commands.UpdateManualTaskAsync(organization.OwnerUserId, organization.Id, template.Id, new OpenCRM.Modules.Planning.Contracts.Dtos.UpdateOrganizationSchedulerTaskDto
        {
            Title = "Новое название",
            StartAt = start,
            RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Daily,
            RecurrenceInterval = 1,
            RecurrenceUntil = start.AddDays(2)
        });

        var children = await context.OrganizationSchedulerTasks.AsNoTracking()
            .Where(value => value.RecurrenceParentTaskId == template.Id && !value.IsDeleted)
            .OrderBy(value => value.StartAt)
            .ToListAsync();
        Assert.Contains(children, value => value.Id == child.Id && value.Title == "Изменено вручную"
            && value.StatusCode == OrganizationSchedulerTaskStatusCodes.InProgress);
        Assert.Equal(2, children.Count);
        Assert.All(children.Where(value => value.Id != child.Id), value => Assert.Equal("Новое название", value.Title));
    }

    [Fact]
    public async Task ParallelGenerationSerializesOnOrganizationAndDoesNotDuplicateChildren()
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext);
        var start = DateTime.UtcNow.Date.AddDays(-1).AddHours(9);
        var template = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Параллельный повтор", start);
        template.RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Daily;
        template.RecurrenceInterval = 1;
        seedContext.OrganizationSchedulerTasks.Add(template);
        await seedContext.SaveChangesAsync();

        async Task<int> GenerateAsync()
        {
            await using var context = fixture.CreateContext();
            using var services = fixture.CreateServices(context);
            using var scope = services.CreateScope();
            var recurrence = scope.ServiceProvider.GetRequiredService<ISchedulerRecurrenceService>();
            return await recurrence.GenerateRecurringTaskInstancesAsync(
                organization.OwnerUserId, organization.Id, DateTime.UtcNow.Date.AddDays(3));
        }

        var results = await Task.WhenAll(GenerateAsync(), GenerateAsync());
        var children = await seedContext.OrganizationSchedulerTasks.AsNoTracking()
            .Where(value => value.RecurrenceParentTaskId == template.Id && !value.IsDeleted)
            .Select(value => value.StartAt)
            .ToListAsync();

        Assert.Equal(4, results.Sum());
        Assert.Equal(4, children.Count);
        Assert.Equal(4, children.Distinct().Count());
    }

    [Fact]
    public async Task GenerationWaitsForOrganizationLockHeldByAnotherPostgresTransaction()
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext);
        var start = DateTime.UtcNow.Date.AddDays(-1).AddHours(9);
        var template = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Заблокированная генерация", start);
        template.RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Daily;
        template.RecurrenceInterval = 1;
        seedContext.OrganizationSchedulerTasks.Add(template);
        await seedContext.SaveChangesAsync();

        await using var lockContext = fixture.CreateContext();
        var releaseOrganizationLock = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var organizationLockReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var organizationLockTask = lockContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var lockTransaction = await lockContext.Database.BeginTransactionAsync();
            await lockContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Organizations\" WHERE \"Id\" = {organization.Id} FOR NO KEY UPDATE");
            organizationLockReady.TrySetResult(((NpgsqlConnection)lockContext.Database.GetDbConnection()).ProcessID);
            await releaseOrganizationLock.Task;
            await lockTransaction.RollbackAsync();
        });
        var lockBackendId = await organizationLockReady.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var lockAttempt = new OrganizationLockAttemptInterceptor();
        await using var waitingContext = fixture.CreateContext(lockAttempt);
        await waitingContext.Database.OpenConnectionAsync();
        var waitingBackendId = ((NpgsqlConnection)waitingContext.Database.GetDbConnection()).ProcessID;
        using var services = fixture.CreateServices(waitingContext);
        using var scope = services.CreateScope();
        var recurrence = scope.ServiceProvider.GetRequiredService<ISchedulerRecurrenceService>();
        var generation = recurrence.GenerateRecurringTaskInstancesAsync(
            organization.OwnerUserId, organization.Id, DateTime.UtcNow.Date.AddDays(2));
        try
        {
            await lockAttempt.LockCommand.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var observerContext = fixture.CreateContext();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            var isBlocked = false;
            while (DateTime.UtcNow < deadline)
            {
                var blockingBackendIds = await observerContext.Database.SqlQuery<int>(
                    $"SELECT unnest(pg_blocking_pids({waitingBackendId})) AS \"Value\"").ToListAsync();
                if (blockingBackendIds.Contains(lockBackendId))
                {
                    isBlocked = true;
                    break;
                }

                await Task.Delay(50);
            }

            Assert.True(isBlocked, "PostgreSQL не показал блокирующую транзакцию планировщика.");
        }
        finally
        {
            releaseOrganizationLock.TrySetResult(true);
            await organizationLockTask.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(3, await generation.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task RecurrenceEngineIgnoresMissingChildAndNonRecurringTemplates()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var root = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Обычная задача", DateTime.UtcNow);
        var child = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Дочерняя задача", DateTime.UtcNow);
        child.RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Daily;
        child.RecurrenceParentTask = root;
        var noStart = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Без даты");
        noStart.RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Daily;
        var noRepeat = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Без повторов", DateTime.UtcNow);
        context.OrganizationSchedulerTasks.AddRange(root, child, noStart, noRepeat);
        await context.SaveChangesAsync();
        var engine = new SchedulerRecurrenceEngine(context);

        Assert.Equal(0, await engine.GenerateForTaskAsync(Guid.NewGuid(), DateTime.UtcNow.AddDays(7)));
        Assert.Equal(0, await engine.GenerateForTaskAsync(child.Id, DateTime.UtcNow.AddDays(7)));
        Assert.Equal(0, await engine.GenerateForTaskAsync(noStart.Id, DateTime.UtcNow.AddDays(7)));
        Assert.Equal(0, await engine.GenerateForTaskAsync(noRepeat.Id, DateTime.UtcNow.AddDays(7)));
    }

    [Fact]
    public async Task RecurrenceEngineHonorsUntilAndCapsGeneratedHorizon()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var start = DateTime.UtcNow.Date.AddDays(1).AddHours(9);
        var shortTemplate = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Короткий повтор", start);
        shortTemplate.RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Daily;
        shortTemplate.RecurrenceUntil = start;
        var longTemplate = PlanningTestData.CreateTask(organization, organization.OwnerUser, "Ограниченный повтор", start);
        longTemplate.RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Daily;
        longTemplate.RecurrenceInterval = 1;
        context.OrganizationSchedulerTasks.AddRange(shortTemplate, longTemplate);
        await context.SaveChangesAsync();
        var engine = new SchedulerRecurrenceEngine(context);

        var noChildren = await engine.GenerateForTaskAsync(shortTemplate.Id, start.AddDays(10));
        var cappedChildren = await engine.GenerateForTaskAsync(longTemplate.Id, start.AddDays(400));
        var refreshedShortTemplate = await context.OrganizationSchedulerTasks.AsNoTracking()
            .SingleAsync(value => value.Id == shortTemplate.Id);
        var generatedChildren = await context.OrganizationSchedulerTasks.AsNoTracking()
            .Where(value => value.RecurrenceParentTaskId == longTemplate.Id)
            .ToListAsync();

        Assert.Equal(0, noChildren);
        Assert.Equal(shortTemplate.RecurrenceUntil, refreshedShortTemplate.RecurrenceGeneratedUntil);
        Assert.Equal(366, cappedChildren);
        Assert.Equal(366, generatedChildren.Count);
        Assert.All(generatedChildren, value =>
        {
            Assert.Null(value.ReminderAt);
            Assert.Equal(TimeSpan.FromHours(1), value.EndAt!.Value - value.StartAt!.Value);
        });
    }

    private sealed class OrganizationLockAttemptInterceptor : DbCommandInterceptor
    {
        internal TaskCompletionSource<bool> LockCommand { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal))
            {
                this.LockCommand.TrySetResult(true);
            }

            return ValueTask.FromResult(result);
        }
    }
}
