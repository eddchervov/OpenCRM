using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Planning.Contracts;

namespace OpenCRM.Modules.Planning.Tests.Integration;

[Collection(PlanningDatabaseCollection.Name)]
public sealed class SchedulerWorkflowTests(PlanningDatabaseFixture fixture)
{
    [Fact]
    public async Task DeferUpsertsWorkflowStateAndCanClearTheDeferredDate()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var vehicle = await PlanningTestData.CreateVehicleAsync(context, organization, organization.OwnerUser);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<ISchedulerWorkflowCommandService>();
        var today = DateTime.UtcNow.Date;
        var firstDeferredDate = today.AddDays(1);
        var updatedDeferredDate = today.AddDays(3);

        var firstResult = await workflow.DeferTaskAsync(organization.OwnerUserId, organization.Id, vehicle.Id,
            "maintenance", null, "vehicle-check", firstDeferredDate, today);
        var updatedResult = await workflow.DeferTaskAsync(organization.OwnerUserId, organization.Id, vehicle.Id,
            "maintenance", null, "vehicle-check", updatedDeferredDate, today);
        var resetResult = await workflow.DeferTaskAsync(organization.OwnerUserId, organization.Id, vehicle.Id,
            "maintenance", null, "vehicle-check", null, today);
        var state = await context.OrganizationSchedulerTaskStates.AsNoTracking()
            .SingleAsync(value => value.OrganizationId == organization.Id && value.TriggerKey == "vehicle-check");

        Assert.Equal(firstDeferredDate, firstResult);
        Assert.Equal(updatedDeferredDate, updatedResult);
        Assert.Null(resetResult);
        Assert.Equal(1, await context.OrganizationSchedulerTaskStates.CountAsync(value => value.TriggerKey == "vehicle-check"));
        Assert.Equal(OrganizationSchedulerTaskStatusCodes.New, state.StatusCode);
        Assert.Null(state.DeferredUntil);
        Assert.Equal("maintenance", state.SourceCode);
    }

    [Fact]
    public async Task DeferRejectsPastDatesAndUpdateWorkflowPersistsStatus()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var vehicle = await PlanningTestData.CreateVehicleAsync(context, organization, organization.OwnerUser);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<ISchedulerWorkflowCommandService>();
        var today = DateTime.UtcNow.Date;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.DeferTaskAsync(
            organization.OwnerUserId, organization.Id, vehicle.Id, "maintenance", null, "past-task", today.AddDays(-1), today));
        await workflow.UpdateTaskWorkflowAsync(organization.OwnerUserId, organization.Id, vehicle.Id,
            "maintenance", null, "past-task", OrganizationSchedulerTaskStatusCodes.Closed);
        var state = await context.OrganizationSchedulerTaskStates.AsNoTracking()
            .SingleAsync(value => value.OrganizationId == organization.Id && value.TriggerKey == "past-task");

        Assert.Equal("Нельзя отложить задачу на прошедшую дату.", exception.Message);
        Assert.Equal(OrganizationSchedulerTaskStatusCodes.Closed, state.StatusCode);
        Assert.NotNull(state.ClosedAt);
        Assert.Null(state.DeferredUntil);
    }

    [Fact]
    public async Task TriggerCannotBeReassignedToAnotherVehicleAndForeignOrganizationIsNotUsedForExistingState()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        var vehicle = await PlanningTestData.CreateVehicleAsync(context, organization, organization.OwnerUser);
        var foreignVehicle = await PlanningTestData.CreateVehicleAsync(context, foreignOrganization, foreignOrganization.OwnerUser);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<ISchedulerWorkflowCommandService>();
        await workflow.UpdateTaskWorkflowAsync(organization.OwnerUserId, organization.Id, vehicle.Id,
            "maintenance", null, "stable-trigger", OrganizationSchedulerTaskStatusCodes.InProgress);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.UpdateTaskWorkflowAsync(
            organization.OwnerUserId, organization.Id, foreignVehicle.Id,
            "maintenance", null, "stable-trigger", OrganizationSchedulerTaskStatusCodes.Closed));

        Assert.Equal("Ключ задачи планировщика уже связан с другой техникой.", exception.Message);
        var state = await context.OrganizationSchedulerTaskStates.AsNoTracking()
            .SingleAsync(value => value.OrganizationId == organization.Id && value.TriggerKey == "stable-trigger");
        Assert.Equal(vehicle.Id, state.VehicleId);
        Assert.Equal(OrganizationSchedulerTaskStatusCodes.InProgress, state.StatusCode);
    }

    [Fact]
    public async Task NewWorkflowTriggerRejectsVehicleOwnedByAnotherOrganization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        var foreignVehicle = await PlanningTestData.CreateVehicleAsync(context, foreignOrganization, foreignOrganization.OwnerUser);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<ISchedulerWorkflowCommandService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.UpdateTaskWorkflowAsync(
            organization.OwnerUserId, organization.Id, foreignVehicle.Id,
            "maintenance", null, "foreign-vehicle-trigger", OrganizationSchedulerTaskStatusCodes.InProgress));

        Assert.Equal("Выбранная техника не найдена в рабочем пространстве.", exception.Message);
        Assert.False(await context.OrganizationSchedulerTaskStates.AsNoTracking()
            .AnyAsync(value => value.OrganizationId == organization.Id));
        Assert.False(await context.OrganizationAuditEvents.AsNoTracking()
            .AnyAsync(value => value.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task TriggerKeyCanUseItsFullLengthAndAuditReferencesStableStateIdAcrossRepeatedDeferral()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var vehicle = await PlanningTestData.CreateVehicleAsync(context, organization, organization.OwnerUser);
        var triggerKey = $"planning-{new string('x', 247)}";
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<ISchedulerWorkflowCommandService>();
        var today = DateTime.UtcNow.Date;

        await workflow.DeferTaskAsync(organization.OwnerUserId, organization.Id, vehicle.Id,
            "maintenance", null, triggerKey, today.AddDays(1), today);
        await workflow.DeferTaskAsync(organization.OwnerUserId, organization.Id, vehicle.Id,
            "maintenance", null, triggerKey, today.AddDays(2), today);

        var state = await context.OrganizationSchedulerTaskStates.AsNoTracking()
            .SingleAsync(value => value.OrganizationId == organization.Id && value.TriggerKey == triggerKey);
        var auditEvents = await context.OrganizationAuditEvents.AsNoTracking()
            .Where(value => value.OrganizationId == organization.Id)
            .ToListAsync();

        Assert.Equal(256, triggerKey.Length);
        Assert.Equal(OrganizationSchedulerTaskStatusCodes.Deferred, state.StatusCode);
        Assert.Equal(today.AddDays(2), state.DeferredUntil);
        Assert.Equal(2, auditEvents.Count);
        Assert.All(auditEvents, value => Assert.Equal(state.Id.ToString("D"), value.TargetId));
    }

    [Fact]
    public async Task WorkflowCanSetDeferredStatusWithoutASeparateDeadline()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var vehicle = await PlanningTestData.CreateVehicleAsync(context, organization, organization.OwnerUser);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<ISchedulerWorkflowCommandService>();

        await workflow.UpdateTaskWorkflowAsync(organization.OwnerUserId, organization.Id, vehicle.Id,
            "maintenance", null, "deferred-without-date", OrganizationSchedulerTaskStatusCodes.Deferred);

        var state = await context.OrganizationSchedulerTaskStates.AsNoTracking()
            .SingleAsync(value => value.OrganizationId == organization.Id && value.TriggerKey == "deferred-without-date");
        Assert.Equal(OrganizationSchedulerTaskStatusCodes.Deferred, state.StatusCode);
        Assert.Null(state.DeferredUntil);
    }

    [Fact]
    public async Task ExistingForeignOrganizationTriggerDoesNotPreventLocalTriggerCreation()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        var vehicle = await PlanningTestData.CreateVehicleAsync(context, organization, organization.OwnerUser);
        var foreignVehicle = await PlanningTestData.CreateVehicleAsync(context, foreignOrganization, foreignOrganization.OwnerUser);
        context.OrganizationSchedulerTaskStates.Add(new OrganizationSchedulerTaskState
        {
            OrganizationId = foreignOrganization.Id,
            VehicleId = foreignVehicle.Id,
            UserId = foreignOrganization.OwnerUserId,
            TriggerKey = "same-trigger-key",
            SourceCode = "maintenance",
            ProcessCode = "detailing",
            WorkflowStageCode = "new"
        });
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<ISchedulerWorkflowCommandService>();

        await workflow.UpdateTaskWorkflowAsync(organization.OwnerUserId, organization.Id, vehicle.Id,
            "maintenance", null, "same-trigger-key", OrganizationSchedulerTaskStatusCodes.InProgress);

        var states = await context.OrganizationSchedulerTaskStates.AsNoTracking()
            .Where(value => value.TriggerKey == "same-trigger-key")
            .ToListAsync();
        Assert.Equal(2, states.Count);
        Assert.Contains(states, value => value.OrganizationId == organization.Id && value.VehicleId == vehicle.Id);
        Assert.Contains(states, value => value.OrganizationId == foreignOrganization.Id && value.VehicleId == foreignVehicle.Id);
    }
}
