using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceWorkflowEdgeTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Theory]
    [InlineData("Недоступный пост", false)]
    [InlineData("Пост 2", true)]
    [InlineData("Пост 1", true)]
    [InlineData(" ", true)]
    public async Task RescheduleValidatesChangedPostAndPreservesDuration(string location, bool isAllowed)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceWorkflowEdgeTests.CreateWork();
        context.Maintenance.Add(work);
        context.OrganizationScenarioCatalogItems.Add(new OrganizationScenarioCatalogItem
        {
            Organization = work.Vehicle.Organization!,
            KindCode = OrganizationScenarioCatalogKindCodes.WorkPost,
            Name = "Пост 2",
            IsActive = true
        });
        await context.SaveChangesAsync();
        using var scope = MaintenanceWorkflowEdgeTests.CreateScope(context);
        var request = new MaintenanceWorkflowActionDto
        {
            Action = "reschedule",
            Version = work.Version,
            PlannedDate = work.PlannedDate!.Value.AddDays(2),
            PlannedDateText = "15.10.2026",
            Location = location
        };
        var previousVersion = work.Version;
        var service = scope.Get<IMaintenanceWorkflowService>();

        if (!isAllowed)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeWorkflow(
                work.User.Id, work.Vehicle.OrganizationId, work.Id, request));
            Assert.Contains("пост недоступен", error.Message);
            context.ChangeTracker.Clear();
            var unchanged = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
            Assert.Equal("Пост 1", unchanged.Location);
            Assert.Equal(previousVersion, unchanged.Version);
            Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(value => value.MaintenanceId == work.Id));

            return;
        }

        await service.ChangeWorkflow(work.User.Id, work.Vehicle.OrganizationId, work.Id, request);

        var saved = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc), saved.PlannedDate);
        Assert.Equal(saved.PlannedDate!.Value.AddDays(1), saved.PlannedEndDate);
        Assert.Equal(location.Trim().Length == 0 ? null : location, saved.Location);
        Assert.NotEqual(previousVersion, saved.Version);
    }

    [Fact]
    public async Task WorkflowRejectsMissingOrderAndStaleVersionWithoutWritingHistory()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceWorkflowEdgeTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceWorkflowEdgeTests.CreateScope(context);
        var service = scope.Get<IMaintenanceWorkflowService>();

        await Assert.ThrowsAsync<Exception>(() => service.ChangeWorkflow(
            work.User.Id, work.Vehicle.OrganizationId, Guid.NewGuid(), new MaintenanceWorkflowActionDto { Action = "start" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeWorkflow(
            work.User.Id, work.Vehicle.OrganizationId, work.Id,
            new MaintenanceWorkflowActionDto { Action = "start", Version = work.Version + 1 }));

        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(value => value.MaintenanceId == work.Id));
        Assert.Equal(MaintenanceStatus.Planned, (await context.Maintenance.AsNoTracking().SingleAsync(value => value.Id == work.Id)).Status);
    }

    [Theory]
    [InlineData("complete", false, false, OrganizationTypeCodes.ServiceStation)]
    [InlineData("complete", true, false, OrganizationTypeCodes.ServiceStation)]
    [InlineData("simple_handover", false, false, OrganizationTypeCodes.ServiceStation)]
    [InlineData("simple_handover", true, false, OrganizationTypeCodes.ServiceStation)]
    [InlineData("complete", false, false, OrganizationTypeCodes.Detailing)]
    [InlineData("complete", true, false, OrganizationTypeCodes.Detailing)]
    [InlineData("simple_handover", false, false, OrganizationTypeCodes.Detailing)]
    [InlineData("simple_handover", true, false, OrganizationTypeCodes.Detailing)]
    public async Task WorkspaceCompletionRequiresAtLeastOnePricedService(
        string action,
        bool hasService,
        bool hasPrice,
        string scenarioCode)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceWorkflowEdgeTests.CreateWork();
        work.Vehicle.Organization!.PrimaryScenarioCode = scenarioCode;
        if (hasService)
        {
            work.ReplaceDetails([new MaintenanceDetail
            {
                DetailId = Guid.NewGuid(),
                Name = "Диагностика",
                Price = hasPrice ? 1000 : null
            }]);
        }

        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceWorkflowEdgeTests.CreateScope(context);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.Get<IMaintenanceWorkflowService>().ChangeWorkflow(work.User.Id, work.Vehicle.OrganizationId, work.Id,
                new MaintenanceWorkflowActionDto { Action = action, Version = work.Version }));

        Assert.Contains(hasService ? "цену каждой работы" : "хотя бы одну выполненную работу", error.Message);
        context.ChangeTracker.Clear();
        Assert.Equal(MaintenanceStatus.Planned, (await context.Maintenance.AsNoTracking().SingleAsync(value => value.Id == work.Id)).Status);
        Assert.Empty(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FleetCompletionPreservesExistingRulesWithoutRequiringPricedServices(bool hasUnpricedService)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceWorkflowEdgeTests.CreateWork();
        work.Vehicle.Organization!.PrimaryScenarioCode = OrganizationTypeCodes.Fleet;
        if (hasUnpricedService)
        {
            work.ReplaceDetails([new MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Внутренний осмотр", Price = null }]);
        }
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceWorkflowEdgeTests.CreateScope(context);

        await scope.Get<IMaintenanceWorkflowService>().ChangeWorkflow(work.User.Id, work.Vehicle.OrganizationId, work.Id,
            new MaintenanceWorkflowActionDto { Action = "complete", Version = work.Version });

        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.Include(item => item.Details).SingleAsync(item => item.Id == work.Id);
        Assert.Equal(MaintenanceStatus.Completed, saved.Status);
        Assert.Equal(hasUnpricedService ? 1 : 0, saved.Details.Count);
        Assert.All(saved.Details, detail => Assert.Null(detail.Price));
    }

    [Theory]
    [InlineData("mark_ready")]
    [InlineData("handover")]
    [InlineData("simple_handover")]
    public async Task WorkspaceHandoverPreservesLegacyCompletedWorkWithoutServices(string action)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceWorkflowEdgeTests.CreateWork();
        work.Status = MaintenanceStatus.Completed;
        work.Date = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        work.CompletedDate = work.Date;
        work.ReadyForHandoverAt = action == "handover" ? work.CompletedDate : null;
        await context.Maintenance.AddAsync(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceWorkflowEdgeTests.CreateScope(context,
            now: new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc));

        await scope.Get<IMaintenanceWorkflowService>().ChangeWorkflow(work.User.Id, work.Vehicle.OrganizationId, work.Id,
            new MaintenanceWorkflowActionDto { Action = action, Version = work.Version });

        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.AsNoTracking().SingleAsync(value => value.Id == work.Id);
        Assert.Equal(MaintenanceStatus.Completed, saved.Status);
        Assert.Equal(new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), saved.CompletedDate);
        Assert.NotNull(saved.ReadyForHandoverAt);
        Assert.Equal(action != "mark_ready", saved.HandedOverAt.HasValue);
        Assert.Empty(saved.Details);
    }

    [Fact]
    public async Task ReasonOnlyRescheduleWritesHistoryAndRepeatedWorkSynchronizesCounter()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceWorkflowEdgeTests.CreateWork();
        work.PlannedEndDate = work.PlannedDate;
        work.ReplaceDetails([new MaintenanceDetail
        {
            DetailId = Guid.NewGuid(),
            Name = "Диагностика",
            Price = 1000
        }]);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var counters = new Mock<IVehicleCounterCommandService>();
        using var scope = MaintenanceWorkflowEdgeTests.CreateScope(context, counters.Object);
        var service = scope.Get<IMaintenanceWorkflowService>();

        await service.ChangeWorkflow(work.User.Id, work.Vehicle.OrganizationId, work.Id, new MaintenanceWorkflowActionDto
        {
            Action = "reschedule",
            PlannedDate = work.PlannedDate,
            PlannedEndDate = work.PlannedDate,
            Reason = "Клиент подтвердил время"
        });

        Assert.Contains("Клиент подтвердил время", (await context.MaintenanceHistoryEvents.SingleAsync(value => value.MaintenanceId == work.Id)).Description);
        await service.ChangeWorkflow(work.User.Id, work.Vehicle.OrganizationId, work.Id,
            new MaintenanceWorkflowActionDto { Action = "complete", Mileage = 100 });
        await service.ChangeWorkflow(work.User.Id, work.Vehicle.OrganizationId, work.Id,
            new MaintenanceWorkflowActionDto { Action = "return_to_work" });
        await service.ChangeWorkflow(work.User.Id, work.Vehicle.OrganizationId, work.Id,
            new MaintenanceWorkflowActionDto { Action = "complete", Mileage = 101 });

        counters.Verify(value => value.SyncCounterFromMaintenanceAsync(
            work.User.Id, work.Vehicle.OrganizationId, work.VehicleId, work.Id, 101, It.IsAny<DateTime>()), Times.Once);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RescheduleUsesTheExistingDateWhenLegacyPlannedDatesAreMissing(
        bool hasPlannedDate, bool hasPlannedEndDate)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceWorkflowEdgeTests.CreateWork();
        work.PlannedDate = hasPlannedDate ? work.Date : null;
        work.PlannedEndDate = hasPlannedEndDate ? work.Date.AddDays(1) : null;
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceWorkflowEdgeTests.CreateScope(context);
        var targetDate = work.Date.AddDays(2);

        await scope.Get<IMaintenanceWorkflowService>().ChangeWorkflow(
            work.User.Id, work.Vehicle.OrganizationId, work.Id,
            new MaintenanceWorkflowActionDto { Action = "reschedule", PlannedDate = targetDate });

        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(targetDate, saved.PlannedDate);
        Assert.Equal(hasPlannedEndDate ? targetDate.AddDays(1) : targetDate, saved.PlannedEndDate);
        Assert.Single(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
    }

    [Fact]
    public async Task UnstartedOrderCanReturnFromInProgressToPlanned()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceWorkflowEdgeTests.CreateWork();
        work.Status = MaintenanceStatus.InProgress;
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceWorkflowEdgeTests.CreateScope(context);

        await scope.Get<IMaintenanceWorkflowService>().ChangeWorkflow(
            work.User.Id, work.Vehicle.OrganizationId, work.Id,
            new MaintenanceWorkflowActionDto { Action = "return_to_planned", Reason = "Перенос по просьбе клиента" });

        context.ChangeTracker.Clear();
        Assert.Equal(MaintenanceStatus.Planned, (await context.Maintenance.SingleAsync(value => value.Id == work.Id)).Status);
        Assert.Contains("Перенос по просьбе клиента", (await context.MaintenanceHistoryEvents
            .SingleAsync(value => value.MaintenanceId == work.Id)).Description);
    }

    [Fact]
    public async Task UnstartedOrderCanBeCancelledWithRecordedReason()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceWorkflowEdgeTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceWorkflowEdgeTests.CreateScope(context);

        await scope.Get<IMaintenanceWorkflowService>().ChangeWorkflow(
            work.User.Id, work.Vehicle.OrganizationId, work.Id,
            new MaintenanceWorkflowActionDto { Action = "cancel", Reason = "Клиент отменил запись" });

        context.ChangeTracker.Clear();
        Assert.Equal(MaintenanceStatus.Cancelled, (await context.Maintenance.SingleAsync(value => value.Id == work.Id)).Status);
        Assert.Contains("Клиент отменил запись", (await context.MaintenanceHistoryEvents
            .SingleAsync(value => value.MaintenanceId == work.Id)).Description);
    }

    [Theory]
    [InlineData("detailing", MaintenanceStatus.Planned)]
    [InlineData("service_station", MaintenanceStatus.InProgress)]
    [InlineData("detailing", MaintenanceStatus.Completed)]
    public async Task SimpleHandoverCompletesAndIssuesWorkForSimpleScenarios(string scenarioCode, MaintenanceStatus status)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceWorkflowEdgeTests.CreateWork();
        work.Vehicle.Organization!.PrimaryScenarioCode = scenarioCode;
        work.Status = status;
        work.ReplaceDetails([new MaintenanceDetail
        {
            DetailId = Guid.NewGuid(),
            Name = "Диагностика",
            Price = 1000
        }]);

        if (status == MaintenanceStatus.Completed)
        {
            work.CompletedDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
            work.Date = work.CompletedDate.Value;
            work.ReadyForHandoverAt = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        }

        await context.Maintenance.AddAsync(work);
        await context.SaveChangesAsync();
        var counters = new Mock<IVehicleCounterCommandService>();
        var scheduler = new Mock<IMaintenanceSchedulerSyncService>();
        scheduler.Setup(value => value.SyncMaintenanceWorkflowAsync(
                work.User.Id, work.Vehicle.OrganizationId!.Value, work.Id, (int)MaintenanceStatus.Completed))
            .Returns(Task.CompletedTask);
        using var scope = MaintenanceWorkflowEdgeTests.CreateScope(context, counters.Object, scheduler.Object);
        var service = scope.Get<IMaintenanceWorkflowService>();
        var now = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);

        await service.ChangeWorkflow(work.User.Id, work.Vehicle.OrganizationId, work.Id,
            new MaintenanceWorkflowActionDto { Action = "simple_handover", Version = work.Version });

        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(MaintenanceStatus.Completed, saved.Status);
        Assert.Equal(status == MaintenanceStatus.Completed ? work.CompletedDate : now.Date, saved.CompletedDate);
        Assert.Equal(status == MaintenanceStatus.Completed ? work.ReadyForHandoverAt : now, saved.ReadyForHandoverAt);
        Assert.Equal(now, saved.HandedOverAt);
        Assert.Single(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
        scheduler.Verify(value => value.SyncMaintenanceWorkflowAsync(
            work.User.Id, work.Vehicle.OrganizationId!.Value, work.Id, (int)MaintenanceStatus.Completed), Times.Once);
        counters.Verify(value => value.SyncCounterFromMaintenanceAsync(
            work.User.Id, work.Vehicle.OrganizationId, work.VehicleId, work.Id, null, now.Date),
            status == MaintenanceStatus.Completed ? Times.Never : Times.Once);
    }

    [Fact]
    public async Task SimpleHandoverPreservesLegacyCompletionFactsAndDoesNotResyncMileage()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceWorkflowEdgeTests.CreateWork();
        work.Status = MaintenanceStatus.Completed;
        work.Date = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        work.CompletedDate = work.Date;
        work.ReadyForHandoverAt = new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);
        work.Mileage = 35000;
        work.ReplaceDetails([new MaintenanceDetail
        {
            DetailId = Guid.NewGuid(),
            Name = "Диагностика",
            Price = 1000
        }]);
        await context.Maintenance.AddAsync(work);
        await context.SaveChangesAsync();
        var counters = new Mock<IVehicleCounterCommandService>();
        var scheduler = new Mock<IMaintenanceSchedulerSyncService>();
        scheduler.Setup(value => value.SyncMaintenanceWorkflowAsync(
                work.User.Id, work.Vehicle.OrganizationId!.Value, work.Id, (int)MaintenanceStatus.Completed))
            .Returns(Task.CompletedTask);
        using var scope = MaintenanceWorkflowEdgeTests.CreateScope(
            context,
            counters.Object,
            scheduler.Object,
            new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc));

        await scope.Get<IMaintenanceWorkflowService>().ChangeWorkflow(
            work.User.Id, work.Vehicle.OrganizationId, work.Id,
            new MaintenanceWorkflowActionDto { Action = "simple_handover", Version = work.Version });

        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), saved.Date);
        Assert.Equal(new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), saved.CompletedDate);
        Assert.Equal(new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc), saved.ReadyForHandoverAt);
        Assert.Equal(new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc), saved.HandedOverAt);
        Assert.Equal(35000, saved.Mileage);
        counters.Verify(value => value.SyncCounterFromMaintenanceAsync(
            work.User.Id, work.Vehicle.OrganizationId, work.VehicleId, work.Id, It.IsAny<int?>(), It.IsAny<DateTime>()), Times.Never);
        Assert.Single(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
    }

    [Theory]
    [InlineData("staged", "detailing", MaintenanceStatus.Planned)]
    [InlineData("simple", "fleet", MaintenanceStatus.Planned)]
    [InlineData("simple", "service_station", MaintenanceStatus.Cancelled)]
    public async Task SimpleHandoverRejectsStagedFleetAndCancelledWork(
        string workflowMode,
        string scenarioCode,
        MaintenanceStatus status)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceWorkflowEdgeTests.CreateWork();
        work.Vehicle.Organization!.MaintenanceWorkflowMode = workflowMode;
        work.Vehicle.Organization.PrimaryScenarioCode = scenarioCode;
        work.Status = status;
        await context.Maintenance.AddAsync(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceWorkflowEdgeTests.CreateScope(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Get<IMaintenanceWorkflowService>().ChangeWorkflow(
            work.User.Id, work.Vehicle.OrganizationId, work.Id,
            new MaintenanceWorkflowActionDto { Action = "simple_handover", Version = work.Version }));

        context.ChangeTracker.Clear();
        Assert.Equal(status, (await context.Maintenance.SingleAsync(value => value.Id == work.Id)).Status);
        Assert.Empty(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
    }

    [Fact]
    public async Task SimpleHandoverRejectsStaleVersionWithoutWritingHistory()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceWorkflowEdgeTests.CreateWork();
        await context.Maintenance.AddAsync(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceWorkflowEdgeTests.CreateScope(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Get<IMaintenanceWorkflowService>().ChangeWorkflow(
            work.User.Id, work.Vehicle.OrganizationId, work.Id,
            new MaintenanceWorkflowActionDto { Action = "simple_handover", Version = work.Version + 1 }));

        context.ChangeTracker.Clear();
        Assert.Equal(MaintenanceStatus.Planned, (await context.Maintenance.SingleAsync(value => value.Id == work.Id)).Status);
        Assert.Empty(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
    }

    [Fact]
    public async Task SimpleHandoverRejectsUnfinishedTrackedWork()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceWorkflowEdgeTests.CreateWork();
        var organization = work.Vehicle.Organization!;
        var member = new OrganizationMember
        {
            User = work.User,
            Organization = organization,
            RoleCode = OrganizationRoleCodes.Performer
        };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var detail = new MaintenanceDetail { Name = "Полировка", EmployeeId = employee.Id };
        work.ReplaceDetails([detail]);
        context.OrganizationEmployees.Add(employee);
        await context.Maintenance.AddAsync(work);
        await context.SaveChangesAsync();
        context.MaintenanceWorkOperations.Add(new MaintenanceWorkOperation
        {
            Id = detail.DetailId,
            MaintenanceId = work.Id,
            Maintenance = work,
            OrganizationId = organization.Id,
            EmployeeId = employee.Id,
            Status = MaintenanceWorkStatus.InProgress
        });
        await context.SaveChangesAsync();
        using var scope = MaintenanceWorkflowEdgeTests.CreateScope(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Get<IMaintenanceWorkflowService>().ChangeWorkflow(
            work.User.Id, organization.Id, work.Id,
            new MaintenanceWorkflowActionDto { Action = "simple_handover", Version = work.Version }));

        context.ChangeTracker.Clear();
        Assert.Equal(MaintenanceStatus.Planned, (await context.Maintenance.SingleAsync(value => value.Id == work.Id)).Status);
        Assert.Empty(await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync());
    }

    private static MaintenanceTestScope CreateScope(
        OpenCRMContext context,
        IVehicleCounterCommandService? counters = null,
        IMaintenanceSchedulerSyncService? scheduler = null,
        DateTime? now = null) =>
        new(
            new MaintenanceRepository(context),
            counters ?? Mock.Of<IVehicleCounterCommandService>(),
            Mock.Of<ISystemClock>(value => value.UtcNow == (now ?? new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc))),
            context,
            Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<IMaintenanceQueryService>(),
            Mock.Of<IMaintenanceSlotQueryService>(),
            null!,
            scheduler: scheduler);

    private static MaintenanceEntity CreateWork()
    {
        var user = new ApplicationUser();
        var date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        var organization = new Organization
        {
            OwnerUser = user,
            Name = "Переходы состояния",
            MaintenanceWorkflowMode = "simple",
            PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation
        };

        return new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization },
            Status = MaintenanceStatus.Planned,
            Date = date,
            PlannedDate = date,
            PlannedEndDate = date.AddDays(1),
            PlannedStartTime = TimeSpan.FromHours(9),
            PlannedEndTime = TimeSpan.FromHours(10),
            Location = "Пост 1"
        };
    }
}
