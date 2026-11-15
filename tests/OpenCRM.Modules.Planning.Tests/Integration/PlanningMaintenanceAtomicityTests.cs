using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Testing;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Planning.Tests.Integration;

[Collection(PlanningDatabaseCollection.Name)]
public sealed class PlanningMaintenanceAtomicityTests(PlanningDatabaseFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReschedulingAndRealSchedulerSyncCommitOrRollBackTogether(bool failSynchronization)
    {
        var fault = new SchedulerSaveFault();
        await using var context = fixture.CreateContext(fault);
        var organization = await fixture.CreateOrganizationAsync(context);
        var date = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);
        var vehicle = new Vehicle { Organization = organization, User = organization.OwnerUser };
        var work = new MaintenanceEntity
        {
            Vehicle = vehicle,
            User = organization.OwnerUser,
            Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance,
            Date = date,
            PlannedDate = date,
            PlannedStartTime = TimeSpan.FromHours(9),
            PlannedEndTime = TimeSpan.FromHours(10)
        };
        var state = new OrganizationSchedulerTaskState
        {
            Organization = organization,
            Vehicle = vehicle,
            User = organization.OwnerUser,
            Maintenance = work,
            TriggerKey = "atomic-" + Guid.NewGuid().ToString("N"),
            StatusCode = OrganizationSchedulerTaskStatusCodes.Deferred,
            DeferredUntil = date.AddDays(5)
        };
        context.OrganizationSchedulerTaskStates.Add(state);
        await context.SaveChangesAsync();
        using var planning = fixture.CreateServices(context);
        using var planningScope = planning.CreateScope();
        var scheduler = planningScope.ServiceProvider.GetRequiredService<IMaintenanceSchedulerSyncService>();
        using var maintenance = PlanningMaintenanceAtomicityTests.CreateMaintenance(context, scheduler);
        var request = new UpdateMaintenancePlannedDateDto { PlannedDate = date.AddDays(1) };
        fault.IsArmed = failSynchronization;

        if (failSynchronization)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => maintenance.Get<IMaintenanceSchedulingService>()
                .UpdatePlannedDate(organization.OwnerUserId, organization.Id, work.Id, request));
        }
        else
        {
            var status = await maintenance.Get<IMaintenanceSchedulingService>()
                .UpdatePlannedDate(organization.OwnerUserId, organization.Id, work.Id, request);
            Assert.Equal(MaintenanceStatus.Planned, status);
        }

        await using var verification = fixture.CreateContext();
        var savedWork = await verification.Maintenance.AsNoTracking().SingleAsync(value => value.Id == work.Id);
        var savedState = await verification.OrganizationSchedulerTaskStates.AsNoTracking().SingleAsync(value => value.Id == state.Id);
        Assert.Equal(failSynchronization ? date : date.AddDays(1), savedWork.PlannedDate);
        Assert.Equal(failSynchronization ? OrganizationSchedulerTaskStatusCodes.Deferred : OrganizationSchedulerTaskStatusCodes.InProgress,
            savedState.StatusCode);
        Assert.Equal(failSynchronization ? date.AddDays(5) : (DateTime?)null, savedState.DeferredUntil);
        Assert.Equal(failSynchronization ? 0 : 1,
            await verification.MaintenanceHistoryEvents.CountAsync(value => value.MaintenanceId == work.Id));
    }

    private static MaintenanceTestScope CreateMaintenance(OpenCRMContext context, IMaintenanceSchedulerSyncService scheduler)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(DateTime.UtcNow);

        return new MaintenanceTestScope(new MaintenanceRepository(context),
            Mock.Of<IVehicleCounterCommandService>(), clock.Object, context, Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<IMaintenanceQueryService>(), Mock.Of<IMaintenanceSlotQueryService>(),
            Mock.Of<IMaintenanceConsumptionService>(), scheduler: scheduler);
    }

    private sealed class SchedulerSaveFault : SaveChangesInterceptor
    {
        public bool IsArmed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (this.IsArmed && eventData.Context!.ChangeTracker.Entries<OrganizationSchedulerTaskState>()
                .Any(entry => entry.State == EntityState.Modified))
            {
                throw new InvalidOperationException("Сбой сохранения связанных задач.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
