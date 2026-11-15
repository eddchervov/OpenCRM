using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Planning.Contracts;

namespace OpenCRM.Modules.Planning.Tests.Integration;

[Collection(PlanningDatabaseCollection.Name)]
public sealed class MaintenanceSchedulerSyncTests(PlanningDatabaseFixture fixture)
{
    [Fact]
    public async Task LinkSyncsEveryMaintenanceStatusAndResetKeepsWorkflowStateUnlinked()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var vehicle = await PlanningTestData.CreateVehicleAsync(context, organization, organization.OwnerUser);
        var maintenance = await PlanningTestData.CreateMaintenanceAsync(context, vehicle, organization.OwnerUser);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var sync = scope.ServiceProvider.GetRequiredService<IMaintenanceSchedulerSyncService>();

        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await sync.LinkMaintenanceTaskAsync(organization.OwnerUserId, organization.Id, vehicle.Id, maintenance.Id,
                "maintenance", maintenance.Id, "maintenance-first", (int)MaintenanceStatus.Planned);
            await sync.LinkMaintenanceTaskAsync(organization.OwnerUserId, organization.Id, vehicle.Id, maintenance.Id,
                "maintenance", maintenance.Id, "maintenance-second", (int)MaintenanceStatus.Completed);
            await sync.LinkMaintenanceTaskAsync(organization.OwnerUserId, organization.Id, vehicle.Id, maintenance.Id,
                "maintenance", maintenance.Id, "maintenance-first", (int)MaintenanceStatus.Planned);
            var unlinkedMaintenanceId = Guid.NewGuid();
            await sync.SyncMaintenanceWorkflowAsync(organization.OwnerUserId, organization.Id, unlinkedMaintenanceId,
                (int)MaintenanceStatus.Completed);
            await sync.ResetMaintenanceWorkflowAsync(organization.OwnerUserId, organization.Id, unlinkedMaintenanceId);

            var first = await context.OrganizationSchedulerTaskStates.AsNoTracking()
                .SingleAsync(value => value.TriggerKey == "maintenance-first");
            var second = await context.OrganizationSchedulerTaskStates.AsNoTracking()
                .SingleAsync(value => value.TriggerKey == "maintenance-second");
            Assert.Equal(OrganizationSchedulerTaskStatusCodes.InProgress, first.StatusCode);
            Assert.Equal(OrganizationSchedulerTaskStatusCodes.Closed, second.StatusCode);
            Assert.Equal(maintenance.Id, first.MaintenanceId);
            Assert.NotNull(second.ClosedAt);

            foreach (var (maintenanceStatus, expectedStatus) in new[]
            {
                (MaintenanceStatus.Planned, OrganizationSchedulerTaskStatusCodes.InProgress),
                (MaintenanceStatus.InProgress, OrganizationSchedulerTaskStatusCodes.InProgress),
                (MaintenanceStatus.Completed, OrganizationSchedulerTaskStatusCodes.Closed),
                (MaintenanceStatus.Cancelled, OrganizationSchedulerTaskStatusCodes.Closed)
            })
            {
                await sync.SyncMaintenanceWorkflowAsync(organization.OwnerUserId, organization.Id, maintenance.Id, (int)maintenanceStatus);
                var states = await context.OrganizationSchedulerTaskStates.AsNoTracking()
                    .Where(value => value.MaintenanceId == maintenance.Id)
                    .ToListAsync();
                Assert.Equal(2, states.Count);
                Assert.All(states, state => Assert.Equal(expectedStatus, state.StatusCode));
            }

            await sync.ResetMaintenanceWorkflowAsync(organization.OwnerUserId, organization.Id, maintenance.Id);
            await sync.SyncMaintenanceWorkflowAsync(organization.OwnerUserId, organization.Id, maintenance.Id,
                (int)MaintenanceStatus.Completed);
            await transaction.CommitAsync();
        });
        var resetStates = await context.OrganizationSchedulerTaskStates.AsNoTracking()
            .Where(value => value.OrganizationId == organization.Id)
            .ToListAsync();
        Assert.Equal(2, resetStates.Count);
        Assert.All(resetStates, state =>
        {
            Assert.Null(state.MaintenanceId);
            Assert.Equal(OrganizationSchedulerTaskStatusCodes.New, state.StatusCode);
            Assert.Null(state.ClosedAt);
            Assert.Null(state.DeferredUntil);
        });
    }

    [Fact]
    public async Task MaintenanceSyncRequiresCallerTransactionAndAmbientRollbackRemovesLinkedState()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var vehicle = await PlanningTestData.CreateVehicleAsync(context, organization, organization.OwnerUser);
        var maintenance = await PlanningTestData.CreateMaintenanceAsync(context, vehicle, organization.OwnerUser);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var sync = scope.ServiceProvider.GetRequiredService<IMaintenanceSchedulerSyncService>();

        var noTransactionException = await Assert.ThrowsAsync<InvalidOperationException>(() => sync.LinkMaintenanceTaskAsync(
            organization.OwnerUserId, organization.Id, vehicle.Id, maintenance.Id, "maintenance", maintenance.Id,
            "ambient-required", (int)MaintenanceStatus.Planned));
        Assert.Equal("Синхронизация планировщика должна выполняться в транзакции обслуживания.", noTransactionException.Message);

        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await sync.LinkMaintenanceTaskAsync(organization.OwnerUserId, organization.Id, vehicle.Id, maintenance.Id,
                "maintenance", maintenance.Id, "ambient-rollback", (int)MaintenanceStatus.Planned);
            await transaction.RollbackAsync();
        });

        await using var verificationContext = fixture.CreateContext();
        Assert.False(await verificationContext.OrganizationSchedulerTaskStates.AsNoTracking()
            .AnyAsync(value => value.TriggerKey == "ambient-rollback"));
    }

    [Fact]
    public async Task MaintenanceLinkCannotReuseTriggerForAnotherVehicle()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        var vehicle = await PlanningTestData.CreateVehicleAsync(context, organization, organization.OwnerUser);
        var foreignVehicle = await PlanningTestData.CreateVehicleAsync(context, foreignOrganization, foreignOrganization.OwnerUser);
        var maintenance = await PlanningTestData.CreateMaintenanceAsync(context, vehicle, organization.OwnerUser);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var sync = scope.ServiceProvider.GetRequiredService<IMaintenanceSchedulerSyncService>();
        InvalidOperationException? exception = null;
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await sync.LinkMaintenanceTaskAsync(organization.OwnerUserId, organization.Id, vehicle.Id, maintenance.Id,
                "maintenance", maintenance.Id, "vehicle-bound-key", (int)MaintenanceStatus.Planned);

            exception = await Assert.ThrowsAsync<InvalidOperationException>(() => sync.LinkMaintenanceTaskAsync(
                organization.OwnerUserId, organization.Id, foreignVehicle.Id, maintenance.Id,
                "maintenance", maintenance.Id, "vehicle-bound-key", (int)MaintenanceStatus.Completed));
            await transaction.RollbackAsync();
        });

        Assert.Equal("Ключ задачи планировщика уже связан с другой техникой.", exception!.Message);
    }

    [Fact]
    public async Task MaintenanceSyncIgnoresTrackedStatesFromOtherOrganizationsAndUnrelatedMaintenance()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        var vehicle = await PlanningTestData.CreateVehicleAsync(context, organization, organization.OwnerUser);
        var foreignVehicle = await PlanningTestData.CreateVehicleAsync(context, foreignOrganization, foreignOrganization.OwnerUser);
        var maintenance = await PlanningTestData.CreateMaintenanceAsync(context, vehicle, organization.OwnerUser);
        var unrelatedMaintenance = await PlanningTestData.CreateMaintenanceAsync(context, vehicle, organization.OwnerUser);
        var deferredUntil = DateTime.UtcNow.Date.AddDays(2);
        var foreignState = new OrganizationSchedulerTaskState
        {
            OrganizationId = foreignOrganization.Id,
            VehicleId = foreignVehicle.Id,
            UserId = foreignOrganization.OwnerUserId,
            SourceCode = "maintenance",
            TriggerKey = "shared-trigger",
            StatusCode = OrganizationSchedulerTaskStatusCodes.Deferred,
            ProcessCode = "detailing",
            WorkflowStageCode = "deferred",
            DeferredUntil = deferredUntil,
            MaintenanceId = maintenance.Id
        };
        var unrelatedState = new OrganizationSchedulerTaskState
        {
            OrganizationId = organization.Id,
            VehicleId = vehicle.Id,
            UserId = organization.OwnerUserId,
            SourceCode = "maintenance",
            TriggerKey = "unrelated-trigger",
            StatusCode = OrganizationSchedulerTaskStatusCodes.Deferred,
            ProcessCode = "detailing",
            WorkflowStageCode = "deferred",
            DeferredUntil = deferredUntil,
            MaintenanceId = unrelatedMaintenance.Id
        };
        var linkedState = new OrganizationSchedulerTaskState
        {
            OrganizationId = organization.Id,
            VehicleId = vehicle.Id,
            UserId = organization.OwnerUserId,
            SourceCode = "maintenance",
            TriggerKey = "linked-trigger",
            StatusCode = OrganizationSchedulerTaskStatusCodes.Deferred,
            ProcessCode = "detailing",
            WorkflowStageCode = "deferred",
            DeferredUntil = deferredUntil,
            MaintenanceId = maintenance.Id
        };
        context.OrganizationSchedulerTaskStates.AddRange(foreignState, unrelatedState, linkedState);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var sync = scope.ServiceProvider.GetRequiredService<IMaintenanceSchedulerSyncService>();

        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await sync.LinkMaintenanceTaskAsync(organization.OwnerUserId, organization.Id, vehicle.Id, maintenance.Id,
                "maintenance", maintenance.Id, "shared-trigger", (int)MaintenanceStatus.Planned);
            await sync.SyncMaintenanceWorkflowAsync(organization.OwnerUserId, organization.Id, maintenance.Id,
                (int)MaintenanceStatus.InProgress);
            await sync.ResetMaintenanceWorkflowAsync(organization.OwnerUserId, organization.Id, maintenance.Id);
            await transaction.CommitAsync();
        });

        await using var verificationContext = fixture.CreateContext();
        var savedForeignState = await verificationContext.OrganizationSchedulerTaskStates.AsNoTracking()
            .SingleAsync(value => value.Id == foreignState.Id);
        var savedUnrelatedState = await verificationContext.OrganizationSchedulerTaskStates.AsNoTracking()
            .SingleAsync(value => value.Id == unrelatedState.Id);
        var savedLinkedStates = await verificationContext.OrganizationSchedulerTaskStates.AsNoTracking()
            .Where(value => value.OrganizationId == organization.Id
                && (value.Id == linkedState.Id || value.TriggerKey == "shared-trigger"))
            .ToListAsync();

        Assert.Equal(OrganizationSchedulerTaskStatusCodes.Deferred, savedForeignState.StatusCode);
        Assert.Equal(deferredUntil, savedForeignState.DeferredUntil);
        Assert.Equal(OrganizationSchedulerTaskStatusCodes.Deferred, savedUnrelatedState.StatusCode);
        Assert.Equal(deferredUntil, savedUnrelatedState.DeferredUntil);
        Assert.Equal(2, savedLinkedStates.Count);
        Assert.All(savedLinkedStates, value =>
        {
            Assert.Equal(OrganizationSchedulerTaskStatusCodes.New, value.StatusCode);
            Assert.Null(value.MaintenanceId);
            Assert.Null(value.DeferredUntil);
        });
    }
}
