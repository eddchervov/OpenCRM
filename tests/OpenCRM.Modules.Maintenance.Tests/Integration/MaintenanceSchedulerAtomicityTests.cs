using OpenCRM.Modules.Planning.Contracts;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceSchedulerAtomicityTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task CreateRollsBackMaintenanceHistoryDefectLinkAndSchedulerLinkWhenSchedulerFails()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceSchedulerAtomicityTests.CreateWork();
        var defect = new VehicleDefect
        {
            Vehicle = work.Vehicle,
            Organization = work.Vehicle.Organization!,
            User = work.User,
            Title = "Требуется ремонт"
        };
        context.Maintenance.Add(work);
        context.VehicleDefects.Add(defect);
        await context.SaveChangesAsync();

        var triggerKey = $"defect-{Guid.NewGuid():N}";
        var schedulerState = MaintenanceSchedulerAtomicityTests.CreateSchedulerState(work, triggerKey, defect.Id);
        context.OrganizationSchedulerTaskStates.Add(schedulerState);
        await context.SaveChangesAsync();

        var scheduler = new Mock<IMaintenanceSchedulerSyncService>();
        scheduler.Setup(service => service.LinkMaintenanceTaskAsync(
                work.User.Id,
                work.Vehicle.Organization!.Id,
                work.Vehicle.Id,
                It.IsAny<Guid>(),
                "defect",
                defect.Id,
                triggerKey,
                (int)MaintenanceStatus.InProgress))
            .Returns(async (string _, Guid _, Guid _, Guid maintenanceId, string _, Guid? _, string _, int _) =>
            {
                Assert.NotNull(context.Database.CurrentTransaction);
                schedulerState.MaintenanceId = maintenanceId;
                schedulerState.StatusCode = OrganizationSchedulerTaskStatusCodes.InProgress;
                await context.SaveChangesAsync();
                throw new InvalidOperationException("Сбой связывания задачи планировщика.");
            });

        using var service = MaintenanceSchedulerAtomicityTests.CreateService(context, scheduler.Object);
        var request = MaintenanceSchedulerAtomicityTests.CreateRequest(MaintenanceStatus.InProgress);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Get<IMaintenanceCreateService>().Create(
            work.User.Id,
            work.Vehicle.Organization!.Id,
            work.Vehicle.Id,
            request,
            new MaintenanceSchedulerLink("defect", defect.Id, triggerKey)));

        Assert.Equal("Сбой связывания задачи планировщика.", error.Message);
        context.ChangeTracker.Clear();
        Assert.False(await context.Maintenance.AnyAsync(value => value.VehicleId == work.Vehicle.Id && value.Id != work.Id));
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(value => value.VehicleId == work.Vehicle.Id));
        Assert.False(await context.VehicleHistoryEvents.AnyAsync(value => value.VehicleId == work.Vehicle.Id));
        Assert.False(await context.MaintenanceDefects.AnyAsync(value => value.VehicleDefectId == defect.Id));
        var savedDefect = await context.VehicleDefects.SingleAsync(value => value.Id == defect.Id);
        Assert.Equal(VehicleDefectStatusCodes.New, savedDefect.StatusCode);
        var savedTask = await context.OrganizationSchedulerTaskStates.SingleAsync(value => value.Id == schedulerState.Id);
        Assert.Null(savedTask.MaintenanceId);
        Assert.Equal(OrganizationSchedulerTaskStatusCodes.New, savedTask.StatusCode);
    }

    [Fact]
    public async Task UpdateRollsBackStatusAndHistoryWhenSchedulerSyncFails()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceSchedulerAtomicityTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var originalVersion = work.Version;
        var originalPlannedDate = work.PlannedDate;
        var schedulerState = MaintenanceSchedulerAtomicityTests.CreateSchedulerState(work, $"update-{Guid.NewGuid():N}");
        schedulerState.MaintenanceId = work.Id;
        context.OrganizationSchedulerTaskStates.Add(schedulerState);
        await context.SaveChangesAsync();

        var scheduler = new Mock<IMaintenanceSchedulerSyncService>();
        scheduler.Setup(service => service.SyncMaintenanceWorkflowAsync(
                work.User.Id, work.Vehicle.Organization!.Id, work.Id, (int)MaintenanceStatus.InProgress))
            .Returns(async (string _, Guid _, Guid _, int _) =>
            {
                Assert.NotNull(context.Database.CurrentTransaction);
                schedulerState.StatusCode = OrganizationSchedulerTaskStatusCodes.InProgress;
                await context.SaveChangesAsync();
                throw new InvalidOperationException("Сбой синхронизации задачи.");
            });

        using var service = MaintenanceSchedulerAtomicityTests.CreateService(context, scheduler.Object);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Update(
            work.User.Id,
            work.Vehicle.Organization!.Id,
            work.Id,
            MaintenanceSchedulerAtomicityTests.CreateUpdateRequest(work, MaintenanceStatus.InProgress)));

        Assert.Equal("Сбой синхронизации задачи.", error.Message);
        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(MaintenanceStatus.Planned, savedWork.Status);
        Assert.Equal(originalPlannedDate, savedWork.PlannedDate);
        Assert.Equal(originalVersion, savedWork.Version);
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(value => value.MaintenanceId == work.Id));
        var savedTask = await context.OrganizationSchedulerTaskStates.SingleAsync(value => value.Id == schedulerState.Id);
        Assert.Equal(OrganizationSchedulerTaskStatusCodes.New, savedTask.StatusCode);
    }

    [Fact]
    public async Task SchedulingRollsBackDatesAndHistoryWhenSchedulerSyncFails()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceSchedulerAtomicityTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var originalVersion = work.Version;
        var originalDate = work.Date;
        var originalPlannedDate = work.PlannedDate;
        var originalStartTime = work.PlannedStartTime;
        var originalEndTime = work.PlannedEndTime;
        var schedulerState = MaintenanceSchedulerAtomicityTests.CreateSchedulerState(work, $"schedule-{Guid.NewGuid():N}");
        schedulerState.MaintenanceId = work.Id;
        context.OrganizationSchedulerTaskStates.Add(schedulerState);
        await context.SaveChangesAsync();

        var scheduler = new Mock<IMaintenanceSchedulerSyncService>();
        scheduler.Setup(service => service.SyncMaintenanceWorkflowAsync(
                work.User.Id, work.Vehicle.Organization!.Id, work.Id, (int)MaintenanceStatus.Planned))
            .Returns(async (string _, Guid _, Guid _, int _) =>
            {
                Assert.NotNull(context.Database.CurrentTransaction);
                schedulerState.StatusCode = OrganizationSchedulerTaskStatusCodes.InProgress;
                await context.SaveChangesAsync();
                throw new InvalidOperationException("Сбой синхронизации задачи.");
            });

        using var service = MaintenanceSchedulerAtomicityTests.CreateService(context, scheduler.Object);
        var newDate = work.PlannedDate!.Value.AddDays(2);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdatePlannedDate(
            work.User.Id,
            work.Vehicle.Organization!.Id,
            work.Id,
            new UpdateMaintenancePlannedDateDto
            {
                PlannedDate = newDate,
                PlannedStartTime = TimeSpan.FromHours(10),
                PlannedEndTime = TimeSpan.FromHours(11)
            }));

        Assert.Equal("Сбой синхронизации задачи.", error.Message);
        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(originalPlannedDate, savedWork.PlannedDate);
        Assert.Equal(originalDate, savedWork.Date);
        Assert.Equal(originalStartTime, savedWork.PlannedStartTime);
        Assert.Equal(originalEndTime, savedWork.PlannedEndTime);
        Assert.Equal(originalVersion, savedWork.Version);
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(value => value.MaintenanceId == work.Id));
        var savedTask = await context.OrganizationSchedulerTaskStates.SingleAsync(value => value.Id == schedulerState.Id);
        Assert.Equal(OrganizationSchedulerTaskStatusCodes.New, savedTask.StatusCode);
    }

    [Fact]
    public async Task WorkflowRollsBackStartAndHistoryWhenSchedulerSyncFails()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceSchedulerAtomicityTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var originalVersion = work.Version;
        var originalDate = work.Date;
        var originalPlannedDate = work.PlannedDate;
        var schedulerState = MaintenanceSchedulerAtomicityTests.CreateSchedulerState(work, $"workflow-{Guid.NewGuid():N}");
        schedulerState.MaintenanceId = work.Id;
        context.OrganizationSchedulerTaskStates.Add(schedulerState);
        await context.SaveChangesAsync();

        var scheduler = new Mock<IMaintenanceSchedulerSyncService>();
        scheduler.Setup(service => service.SyncMaintenanceWorkflowAsync(
                work.User.Id, work.Vehicle.Organization!.Id, work.Id, (int)MaintenanceStatus.InProgress))
            .Returns(async (string _, Guid _, Guid _, int _) =>
            {
                Assert.NotNull(context.Database.CurrentTransaction);
                schedulerState.StatusCode = OrganizationSchedulerTaskStatusCodes.InProgress;
                await context.SaveChangesAsync();
                throw new InvalidOperationException("Сбой синхронизации задачи.");
            });

        using var service = MaintenanceSchedulerAtomicityTests.CreateService(context, scheduler.Object);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeWorkflow(
            work.User.Id,
            work.Vehicle.Organization!.Id,
            work.Id,
            new MaintenanceWorkflowActionDto { Action = "start" }));

        Assert.Equal("Сбой синхронизации задачи.", error.Message);
        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(MaintenanceStatus.Planned, savedWork.Status);
        Assert.Equal(originalPlannedDate, savedWork.PlannedDate);
        Assert.Equal(originalDate, savedWork.Date);
        Assert.Equal(originalVersion, savedWork.Version);
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(value => value.MaintenanceId == work.Id));
        var savedTask = await context.OrganizationSchedulerTaskStates.SingleAsync(value => value.Id == schedulerState.Id);
        Assert.Equal(OrganizationSchedulerTaskStatusCodes.New, savedTask.StatusCode);
    }

    [Fact]
    public async Task DeleteRollsBackSoftDeleteHistoryAndSchedulerResetWhenSchedulerFails()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceSchedulerAtomicityTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var originalVersion = work.Version;
        var defect = new VehicleDefect
        {
            Vehicle = work.Vehicle,
            Organization = work.Vehicle.Organization!,
            User = work.User,
            Title = "Ожидает устранения"
        };
        context.VehicleDefects.Add(defect);
        await context.SaveChangesAsync();
        context.MaintenanceDefects.Add(new MaintenanceDefect
        {
            MaintenanceId = work.Id,
            VehicleDefectId = defect.Id,
            OrganizationId = work.Vehicle.Organization!.Id,
            UserId = work.User.Id
        });
        var triggerKey = $"delete-{Guid.NewGuid():N}";
        var schedulerState = MaintenanceSchedulerAtomicityTests.CreateSchedulerState(work, triggerKey);
        schedulerState.MaintenanceId = work.Id;
        context.OrganizationSchedulerTaskStates.Add(schedulerState);
        await context.SaveChangesAsync();

        var scheduler = new Mock<IMaintenanceSchedulerSyncService>();
        scheduler.Setup(service => service.ResetMaintenanceWorkflowAsync(work.User.Id, work.Vehicle.Organization!.Id, work.Id))
            .Returns(async (string _, Guid _, Guid _) =>
            {
                Assert.NotNull(context.Database.CurrentTransaction);
                schedulerState.MaintenanceId = null;
                schedulerState.StatusCode = OrganizationSchedulerTaskStatusCodes.New;
                await context.SaveChangesAsync();
                throw new InvalidOperationException("Сбой сброса задачи.");
            });

        using var service = MaintenanceSchedulerAtomicityTests.CreateService(context, scheduler.Object);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Delete(
            work.User.Id,
            work.Vehicle.Organization!.Id,
            work.Id));

        Assert.Equal("Сбой сброса задачи.", error.Message);
        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.False(savedWork.IsDeleted);
        Assert.Equal(originalVersion, savedWork.Version);
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(value => value.MaintenanceId == work.Id));
        Assert.True(await context.MaintenanceDefects.AnyAsync(value =>
            value.MaintenanceId == work.Id && value.VehicleDefectId == defect.Id));
        var savedTask = await context.OrganizationSchedulerTaskStates.SingleAsync(value => value.Id == schedulerState.Id);
        Assert.Equal(work.Id, savedTask.MaintenanceId);
        Assert.Equal(OrganizationSchedulerTaskStatusCodes.New, savedTask.StatusCode);
    }

    private static MaintenanceTestScope CreateService(OpenCRMContext context, IMaintenanceSchedulerSyncService scheduler)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));
        var consumption = new Mock<IMaintenanceConsumptionService>();
        consumption.Setup(service => service.SynchronizeAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<MaintenanceMaterialConsumptionLine>>()))
            .ReturnsAsync(new MaintenanceConsumptionResult(0, []));

        return new MaintenanceTestScope(
            new MaintenanceRepository(context),
            Mock.Of<IVehicleCounterCommandService>(),
            clock.Object,
            context,
            Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<IMaintenanceQueryService>(),
            Mock.Of<IMaintenanceSlotQueryService>(),
            consumption.Object,
            scheduler: scheduler);
    }

    private static MaintenanceEntity CreateWork()
    {
        var user = new ApplicationUser();
        var organization = new Organization
        {
            Name = "Проверка атомарности планировщика",
            PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation,
            OwnerUser = user,
            Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
        };
        var vehicle = new Vehicle
        {
            User = user,
            Organization = organization,
            RegistrationPlate = "А123АА154"
        };
        var plannedDate = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

        return new MaintenanceEntity
        {
            Vehicle = vehicle,
            User = user,
            Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance,
            Date = plannedDate,
            PlannedDate = plannedDate,
            PlannedStartTime = TimeSpan.FromHours(9),
            PlannedEndTime = TimeSpan.FromHours(10),
            DateTimeCreation = plannedDate.AddDays(-1),
            Details = [new() { Name = "Работа", Price = 100 }]
        };
    }

    private static OrganizationSchedulerTaskState CreateSchedulerState(MaintenanceEntity work, string triggerKey, Guid? sourceEntityId = null)
    {
        return new OrganizationSchedulerTaskState
        {
            Organization = work.Vehicle.Organization!,
            Vehicle = work.Vehicle,
            SourceCode = sourceEntityId.HasValue ? "defect" : "maintenance",
            SourceEntityId = sourceEntityId,
            TriggerKey = triggerKey,
            User = work.User
        };
    }

    private static CreateMaintenanceDto CreateRequest(MaintenanceStatus status)
    {
        return new CreateMaintenanceDto
        {
            Status = status,
            Type = MaintenanceType.Maintenance,
            PlannedDate = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc),
            PlannedStartTime = TimeSpan.FromHours(9),
            PlannedEndTime = TimeSpan.FromHours(10),
            CompletedDate = null,
            Location = null,
            Performer = null,
            Mileage = null,
            Comment = null,
            Details = [new CreateMaintenanceDetailDto { Name = "Работа", Price = 100 }]
        };
    }

    private static UpdateMaintenanceDto CreateUpdateRequest(MaintenanceEntity work, MaintenanceStatus status)
    {
        return new UpdateMaintenanceDto
        {
            Status = status,
            Type = work.Type,
            PlannedDate = work.PlannedDate,
            PlannedStartTime = work.PlannedStartTime,
            PlannedEndTime = work.PlannedEndTime,
            PlannedEndDate = work.PlannedEndDate,
            CompletedDate = work.CompletedDate,
            Location = work.Location,
            Performer = work.Performer,
            Mileage = work.Mileage,
            Comment = work.Comment,
            DiscountAmount = work.DiscountAmount,
            PaidAmount = work.PaidAmount,
            LaborCostAmount = work.LaborCostAmount,
            Details = work.Details.Select(detail => new CreateMaintenanceDetailDto
            {
                DetailId = detail.DetailId,
                Name = detail.Name,
                Description = detail.Description,
                Price = detail.Price,
                VatRateCode = detail.VatRateCode,
                EmployeeId = detail.EmployeeId,
                EmployeeName = detail.EmployeeName,
                CatalogItemId = detail.CatalogItemId,
                Quantity = detail.Quantity
            }).ToArray()
        };
    }
}
