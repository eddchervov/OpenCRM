using OpenCRM.Testing;
using OpenCRM.Modules.Workspace.Workday;
using OpenCRM.Modules.Workspace.Queries;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class OccupiedMaintenanceSlotsTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task SlotsUsePlannedDateAndExcludeCompletedCancelledDeletedOtherPostsAndEditedRecord()
    {
        await using var context = fixture.CreateContext();
        var vehicle = OccupiedMaintenanceSlotsTests.CreateVehicle();
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        var planned = OccupiedMaintenanceSlotsTests.CreateWork(vehicle, date);
        var completed = OccupiedMaintenanceSlotsTests.CreateWork(vehicle, date);
        completed.Status = MaintenanceStatus.Completed;
        completed.CompletedDate = date.AddDays(1);
        completed.Date = completed.CompletedDate.Value;
        completed.PlannedStartTime = TimeSpan.FromHours(12);
        completed.PlannedEndTime = null;
        var edited = OccupiedMaintenanceSlotsTests.CreateWork(vehicle, date);
        var cancelled = OccupiedMaintenanceSlotsTests.CreateWork(vehicle, date);
        cancelled.Status = MaintenanceStatus.Cancelled;
        var deleted = OccupiedMaintenanceSlotsTests.CreateWork(vehicle, date);
        deleted.IsDeleted = true;
        var otherPost = OccupiedMaintenanceSlotsTests.CreateWork(vehicle, date);
        otherPost.Location = "Пост 2";
        var unscheduled = OccupiedMaintenanceSlotsTests.CreateWork(vehicle, date);
        unscheduled.PlannedStartTime = null;
        context.Maintenance.AddRange(planned, completed, edited, cancelled, deleted, otherPost, unscheduled,
            OccupiedMaintenanceSlotsTests.CreateWork(vehicle, date.AddDays(1)),
            OccupiedMaintenanceSlotsTests.CreateWork(OccupiedMaintenanceSlotsTests.CreateVehicle(), date));
        await context.SaveChangesAsync();
        var access = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        access.Setup(x => x.EnsureWorkspaceCanReadAsync("reader", vehicle.Organization!.Id, OrganizationPermissionCodes.MaintenanceView))
            .Returns(Task.CompletedTask);
        var service = new MaintenanceSlotQueryService(context, access.Object);

        var slots = await service.GetOccupiedSlots("reader", vehicle.Organization!.Id, DateOnly.FromDateTime(date), "  ПОСТ 1  ", edited.Id);

        Assert.Equal(planned.Id, Assert.Single(slots).Id);
        Assert.Equal(600, slots[0].StartMinute);
        Assert.Equal(660, slots[0].EndMinute);
        Assert.Equal("А123АА154", slots[0].Vehicle);
        Assert.Equal("Полировка", slots[0].Service);
        access.VerifyAll();
    }

    [Fact]
    public async Task AvailabilityIsNotTruncatedToCalendarLimit()
    {
        await using var context = fixture.CreateContext();
        var vehicle = OccupiedMaintenanceSlotsTests.CreateVehicle();
        var date = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);
        for (var index = 0; index < 501; index++)
        {
            context.Maintenance.Add(OccupiedMaintenanceSlotsTests.CreateWork(vehicle, date));
        }

        await context.SaveChangesAsync();
        var access = new Mock<IOrganizationSubscriptionService>();
        var service = new MaintenanceSlotQueryService(context, access.Object);

        var slots = await service.GetOccupiedSlots("reader", vehicle.Organization!.Id, DateOnly.FromDateTime(date), "Пост 1");

        Assert.Equal(501, slots.Count);
    }

    [Fact]
    public async Task MissingPermissionStopsQueryBeforeDatabaseAccess()
    {
        var access = new Mock<IOrganizationSubscriptionService>();
        access.Setup(x => x.EnsureWorkspaceCanReadAsync("denied", It.IsAny<Guid>(), OrganizationPermissionCodes.MaintenanceView))
            .ThrowsAsync(new InvalidOperationException("Нет доступа"));
        var service = new MaintenanceSlotQueryService(null!, access.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetOccupiedSlots(
            "denied", Guid.NewGuid(), new DateOnly(2026, 9, 11), "Пост 1"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CalendarKeepsCompletedHistoryWithPlannedDateFallbackAndAvailabilityReleasesPost(bool hasPlannedDate)
    {
        await using var context = fixture.CreateContext();
        var vehicle = OccupiedMaintenanceSlotsTests.CreateVehicle();
        var plannedDate = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc);
        var completedDate = plannedDate.AddMonths(4);
        var work = OccupiedMaintenanceSlotsTests.CreateWork(vehicle, completedDate);
        work.Status = MaintenanceStatus.Completed;
        work.PlannedDate = hasPlannedDate ? plannedDate : null;
        work.CompletedDate = completedDate;
        work.PlannedStartTime = TimeSpan.FromHours(9);
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var availability = new MaintenanceSlotQueryService(context, Mock.Of<IOrganizationSubscriptionService>());
        var calendar = new OrganizationWorkspaceQueryService(
            new OrganizationWorkdayQueryService(context),
            new OrganizationWorkspaceMaintenanceQueryService(context),
            new OrganizationFinanceQueryService(context),
            new OrganizationMaintenanceJournalQueryService(context),
            new OrganizationWorkJournalQueryService(context),
            new OrganizationWorkspaceAnalyticsQueryService(context),
            new OrganizationWorkspacePlanningQueryService(context));

        foreach (var date in new[] { plannedDate, completedDate })
        {
            var slots = await availability.GetOccupiedSlots("reader", vehicle.Organization!.Id, DateOnly.FromDateTime(date), "Пост 1");
            var records = await calendar.GetMaintenanceCalendarAsync(vehicle.Organization.Id, date, date, includeAllStatuses: true);
            if (date != (hasPlannedDate ? plannedDate : completedDate))
            {
                Assert.Empty(slots);
                Assert.Empty(records);
                continue;
            }

            Assert.Empty(slots);
            Assert.Equal(work.Id, Assert.Single(records).Id);
            Assert.Equal(date, records[0].Date);
        }
    }

    [Fact]
    public async Task MultiDayAvailabilityUsesWholePeriodAndExclusiveEnd()
    {
        await using var context = fixture.CreateContext();
        var vehicle = OccupiedMaintenanceSlotsTests.CreateVehicle();
        var date = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);
        var work = OccupiedMaintenanceSlotsTests.CreateWork(vehicle, date);
        work.PlannedEndDate = date.AddDays(2);
        work.PlannedEndTime = TimeSpan.Zero;
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var service = new MaintenanceSlotQueryService(context, Mock.Of<IOrganizationSubscriptionService>());
        var all = await service.GetOccupiedSlots("reader", vehicle.Organization!.Id, DateOnly.FromDateTime(date), "Пост 1", dateTo: DateOnly.FromDateTime(date.AddDays(2)));
        Assert.Equal(600, Assert.Single(all).StartMinute);
        Assert.Equal(2880, all[0].EndMinute);
        Assert.Empty(await service.GetOccupiedSlots("reader", vehicle.Organization.Id, DateOnly.FromDateTime(date.AddDays(2)), "Пост 1"));
        var calendar = new OrganizationWorkspaceQueryService(
            new OrganizationWorkdayQueryService(context),
            new OrganizationWorkspaceMaintenanceQueryService(context),
            new OrganizationFinanceQueryService(context),
            new OrganizationMaintenanceJournalQueryService(context),
            new OrganizationWorkJournalQueryService(context),
            new OrganizationWorkspaceAnalyticsQueryService(context),
            new OrganizationWorkspacePlanningQueryService(context));
        Assert.Empty(await calendar.GetMaintenanceCalendarAsync(vehicle.Organization.Id, date.AddDays(2), date.AddDays(2), includeAllStatuses: true));
        Assert.Single(await calendar.GetMaintenanceCalendarAsync(vehicle.Organization.Id, date.AddDays(1), date.AddDays(1), includeAllStatuses: true));
    }

    [Theory]
    [InlineData(null, null, "Автомобиль", false)]
    [InlineData(null, "Техника без номера", "Техника без номера", true)]
    public async Task SlotsUseVehicleFallbackAndAllowMissingService(
        string? plate,
        string? name,
        string expectedVehicle,
        bool materialOnly)
    {
        await using var context = fixture.CreateContext();
        var vehicle = OccupiedMaintenanceSlotsTests.CreateVehicle();
        vehicle.RegistrationPlate = plate;
        vehicle.Name = name;
        var date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        var work = OccupiedMaintenanceSlotsTests.CreateWork(vehicle, date);
        work.Details = materialOnly ? [new MaintenanceDetail { Name = "Материал", MaterialName = "Состав" }] : [];
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var service = new MaintenanceSlotQueryService(context, Mock.Of<IOrganizationSubscriptionService>());

        var slots = await service.GetOccupiedSlots("reader", vehicle.Organization!.Id, DateOnly.FromDateTime(date), work.Location!);

        var slot = Assert.Single(slots);
        Assert.Equal(expectedVehicle, slot.Vehicle);
        Assert.Null(slot.Service);
    }

    [Fact]
    public async Task AvailabilityExcludesIntervalsOutsideTheRequestedDay()
    {
        await using var context = fixture.CreateContext();
        var vehicle = OccupiedMaintenanceSlotsTests.CreateVehicle();
        var day = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        var previousNight = OccupiedMaintenanceSlotsTests.CreateWork(vehicle, day.AddDays(-1));
        previousNight.PlannedStartTime = TimeSpan.FromHours(23);
        previousNight.PlannedEndDate = day;
        previousNight.PlannedEndTime = TimeSpan.Zero;
        var legacyInterval = OccupiedMaintenanceSlotsTests.CreateWork(vehicle, day);
        legacyInterval.PlannedStartTime = TimeSpan.FromDays(1);
        legacyInterval.PlannedEndTime = TimeSpan.FromHours(25);
        context.Maintenance.AddRange(previousNight, legacyInterval);
        await context.SaveChangesAsync();
        var service = new MaintenanceSlotQueryService(context, Mock.Of<IOrganizationSubscriptionService>());

        var slots = await service.GetOccupiedSlots("reader", vehicle.Organization!.Id, DateOnly.FromDateTime(day), "Пост 1");

        Assert.Empty(slots);
    }

    private static Vehicle CreateVehicle()
    {
        var user = new ApplicationUser();

        return new Vehicle
        {
            User = user,
            Organization = new Organization
            {
                Name = "Проверка занятости",
                OwnerUser = user,
                Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
            },
            RegistrationPlate = "А123АА154"
        };
    }

    private static MaintenanceEntity CreateWork(Vehicle vehicle, DateTime date)
    {
        return new MaintenanceEntity
        {
            Vehicle = vehicle,
            User = vehicle.User,
            Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Other,
            Date = date,
            PlannedDate = date,
            DateTimeCreation = date,
            PlannedStartTime = TimeSpan.FromHours(10),
            PlannedEndTime = TimeSpan.FromHours(11),
            Location = "Пост 1",
            Details = [new() { Name = "Полировка" }]
        };
    }
}
