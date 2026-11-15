using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Helpers;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Modules.Planning.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Planning.Tests.Integration;

[Collection(PlanningDatabaseCollection.Name)]
public sealed class SchedulerTaskCommandTests(PlanningDatabaseFixture fixture)
{
    [Fact]
    public async Task ManualTaskCommandsCreateUpdateRescheduleSendReminderAndSoftDeleteAtomically()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<ISchedulerTaskCommandService>();
        var start = DateTime.UtcNow.Date.AddDays(1).AddHours(10);

        var taskId = await commands.CreateTaskAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationSchedulerTaskDto
        {
            Title = "  Позвонить клиенту  ",
            Comment = "   ",
            StartAt = start,
            EndAt = start.AddHours(1),
            ReminderAt = start.AddMinutes(-15)
        });
        await commands.UpdateManualTaskAsync(organization.OwnerUserId, organization.Id, taskId, new UpdateOrganizationSchedulerTaskDto
        {
            Title = "  Подтвердить запись  ",
            Comment = "   Клиент ожидает звонка.   ",
            StartAt = start,
            EndAt = start.AddHours(2),
            ReminderAt = start.AddMinutes(-15)
        });
        await commands.UpdateManualTaskStatusAsync(organization.OwnerUserId, organization.Id, taskId, OrganizationSchedulerTaskStatusCodes.InProgress);
        await commands.UpdateManualTaskScheduleAsync(organization.OwnerUserId, organization.Id, taskId,
            new UpdateOrganizationSchedulerTaskScheduleDto { StartAt = start.AddDays(1), EndAt = start.AddDays(1) });
        var sentAt = DateTime.UtcNow;
        await commands.MarkReminderSentAsync(organization.OwnerUserId, organization.Id, taskId, sentAt);
        await commands.UpdateManualTaskStatusAsync(organization.OwnerUserId, organization.Id, taskId, OrganizationSchedulerTaskStatusCodes.Closed);

        var task = await context.OrganizationSchedulerTasks.AsNoTracking().SingleAsync(value => value.Id == taskId);
        Assert.Equal("Подтвердить запись", task.Title);
        Assert.Equal("Клиент ожидает звонка.", task.Comment);
        Assert.Equal(OrganizationSchedulerTaskStatusCodes.Closed, task.StatusCode);
        Assert.NotNull(task.ClosedAt);
        Assert.Equal(start.AddDays(1), task.StartAt);
        Assert.Equal(start.AddDays(1).AddHours(1), task.EndAt);
        Assert.Equal(DateTimeKind.Utc, task.ReminderSentAt!.Value.Kind);
        Assert.InRange(Math.Abs((sentAt - task.ReminderSentAt.Value).Ticks), 0, 10);

        await commands.DeleteManualTaskAsync(organization.OwnerUserId, organization.Id, taskId);

        var deletedTask = await context.OrganizationSchedulerTasks.AsNoTracking().SingleAsync(value => value.Id == taskId);
        Assert.True(deletedTask.IsDeleted);
        Assert.NotNull(deletedTask.DeletedAt);
        Assert.Equal(7, await context.OrganizationAuditEvents.CountAsync(value => value.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task DeletingRecurringTemplateSoftDeletesGeneratedChildren()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<ISchedulerTaskCommandService>();
        var start = DateTime.UtcNow.Date.AddDays(1).AddHours(9);
        var taskId = await commands.CreateTaskAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationSchedulerTaskDto
        {
            Title = "Повторяющаяся задача",
            StartAt = start,
            RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Daily,
            RecurrenceInterval = 1,
            RecurrenceUntil = start.AddDays(3)
        });
        var childIds = await context.OrganizationSchedulerTasks.AsNoTracking()
            .Where(value => value.RecurrenceParentTaskId == taskId && !value.IsDeleted)
            .Select(value => value.Id)
            .ToListAsync();
        Assert.NotEmpty(childIds);

        await commands.DeleteManualTaskAsync(organization.OwnerUserId, organization.Id, taskId);

        Assert.True(await context.OrganizationSchedulerTasks.AsNoTracking().Where(value => value.Id == taskId).Select(value => value.IsDeleted).SingleAsync());
        Assert.All(await context.OrganizationSchedulerTasks.AsNoTracking().Where(value => childIds.Contains(value.Id)).ToListAsync(),
            value => Assert.True(value.IsDeleted));
    }

    [Fact]
    public async Task DisabledSchedulerFeaturePreventsCatalogInitializationAndWrites()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var permissions = new Mock<IOrganizationSubscriptionService>();
        permissions.Setup(value => value.HasFeatureAsync(organization.OwnerUserId, organization.Id, FeatureCodes.SchedulerModule))
            .ReturnsAsync(false);
        using var services = fixture.CreateServices(context, permissions.Object);
        using var scope = services.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<ISchedulerTaskCommandService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => commands.CreateTaskAsync(
            organization.OwnerUserId, organization.Id, new CreateOrganizationSchedulerTaskDto { Title = "Задача" }));

        Assert.Equal("Раздел планирования недоступен для рабочего пространства.", exception.Message);
        Assert.Equal(0, await context.OrganizationScenarioCatalogItems.CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(0, await context.OrganizationSchedulerTasks.CountAsync(value => value.OrganizationId == organization.Id));
        Assert.Equal(0, await context.OrganizationAuditEvents.CountAsync(value => value.OrganizationId == organization.Id));
        permissions.Verify(value => value.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task WritePermissionIsCheckedBeforeCatalogDefaultsAndTaskMutation()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var permissions = PlanningDatabaseFixture.CreateEnabledPermissions();
        permissions.Setup(value => value.EnsureWorkspaceCanWriteAsync(organization.OwnerUserId, organization.Id, It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Недостаточно прав."));
        using var services = fixture.CreateServices(context, permissions.Object);
        using var scope = services.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<ISchedulerTaskCommandService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => commands.CreateTaskAsync(
            organization.OwnerUserId, organization.Id, new CreateOrganizationSchedulerTaskDto { Title = "Задача" }));

        Assert.Equal("Недостаточно прав.", exception.Message);
        Assert.Empty(await context.OrganizationSchedulerTasks.Where(value => value.OrganizationId == organization.Id).ToListAsync());
        Assert.Empty(await context.OrganizationAuditEvents.Where(value => value.OrganizationId == organization.Id).ToListAsync());
        Assert.Empty(await context.OrganizationScenarioCatalogItems.Where(value => value.OrganizationId == organization.Id).ToListAsync());
    }

    [Fact]
    public async Task MarkReminderRejectsTaskWithoutReminderAndManualCommandRejectsMissingTask()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<ISchedulerTaskCommandService>();
        var taskId = await commands.CreateTaskAsync(organization.OwnerUserId, organization.Id,
            new CreateOrganizationSchedulerTaskDto { Title = "Без напоминания" });

        var noReminder = await Assert.ThrowsAsync<InvalidOperationException>(() => commands.MarkReminderSentAsync(
            organization.OwnerUserId, organization.Id, taskId, DateTime.UtcNow));
        var missingTask = await Assert.ThrowsAsync<InvalidOperationException>(() => commands.UpdateManualTaskStatusAsync(
            organization.OwnerUserId, organization.Id, Guid.NewGuid(), OrganizationSchedulerTaskStatusCodes.Closed));

        Assert.Equal("У задачи нет напоминания.", noReminder.Message);
        Assert.Equal("Задача планировщика не найдена.", missingTask.Message);
        Assert.False(await context.OrganizationSchedulerTasks.AsNoTracking()
            .Where(value => value.Id == taskId)
            .Select(value => value.ReminderSentAt.HasValue)
            .SingleAsync());
    }
}
