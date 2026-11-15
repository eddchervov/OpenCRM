using OpenCRM.Testing;
using OpenCRM.Modules.Workspace.Workday;
using OpenCRM.Modules.Workspace.Queries;
using OpenCRM.Modules.Workspace.Access;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Workspace.Shell;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class DetailingWorkdayQueryTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Theory]
    [InlineData("toyota")]
    [InlineData("future-brand")]
    [InlineData(null)]
    public async Task WorkdayUsesLogoKeyFromLinkedBrandRegardlessOfVehicleName(string? logoKey)
    {
        await using var context = fixture.CreateContext();
        var date = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        var vehicle = DetailingWorkdayQueryTests.CreateVehicle();
        vehicle.Brand = new Brand { Name = Guid.NewGuid().ToString(), LogoKey = logoKey };
        context.Maintenance.Add(DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.InProgress, date));
        await context.SaveChangesAsync();
        var organizationId = vehicle.Organization!.Id;
        context.ChangeTracker.Clear();
        var service = new OrganizationWorkdayQueryService(context);

        var day = await service.GetWorkdayAsync(organizationId, date, false);
        var visualization = await service.GetVisualizationAsync(organizationId, date, false);

        Assert.Equal(logoKey, Assert.Single(day.InProgress.Items).VehicleBrandLogoKey);
        Assert.Equal(logoKey, Assert.Single(visualization.InProgress.Items).VehicleBrandLogoKey);
    }

    [Fact]
    public async Task ManualBrandDoesNotReceiveCatalogLogo()
    {
        await using var context = fixture.CreateContext();
        var date = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        var vehicle = DetailingWorkdayQueryTests.CreateVehicle();
        context.Maintenance.Add(DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.InProgress, date));
        await context.SaveChangesAsync();
        var service = new OrganizationWorkdayQueryService(context);

        var day = await service.GetWorkdayAsync(vehicle.Organization!.Id, date, false);

        var item = Assert.Single(day.InProgress.Items);
        Assert.Equal("BMW X3", item.VehicleName);
        Assert.Null(item.VehicleBrandLogoKey);
    }

    [Fact]
    public async Task WorkdaySummarizesOnlyServiceDetailsAndWorkOperationStatuses()
    {
        await using var context = fixture.CreateContext();
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        var vehicle = DetailingWorkdayQueryTests.CreateVehicle();
        var work = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.InProgress, date);
        work.Details =
        [
            new() { Name = "Полировка" },
            new() { Name = "Полировка" },
            new() { Name = "Мойка" },
            new() { Name = "  " },
            new() { Name = "Материал", MaterialName = "Керамика" },
            new() { Name = "Материал справочника", MaterialCatalogItemId = Guid.NewGuid() }
        ];
        context.Maintenance.Add(work);
        context.MaintenanceWorkOperations.AddRange(
            new MaintenanceWorkOperation
            {
                Id = Guid.NewGuid(),
                Maintenance = work,
                OrganizationId = vehicle.Organization!.Id,
                EmployeeId = Guid.NewGuid(),
                Status = MaintenanceWorkStatus.Completed
            },
            new MaintenanceWorkOperation
            {
                Id = Guid.NewGuid(),
                Maintenance = work,
                OrganizationId = vehicle.Organization.Id,
                EmployeeId = Guid.NewGuid(),
                Status = MaintenanceWorkStatus.Paused
            },
            new MaintenanceWorkOperation
            {
                Id = Guid.NewGuid(),
                Maintenance = work,
                OrganizationId = vehicle.Organization.Id,
                EmployeeId = Guid.NewGuid(),
                Status = MaintenanceWorkStatus.Assigned
            });
        await context.SaveChangesAsync();
        var service = new OrganizationWorkdayQueryService(context);

        var result = await service.GetWorkdayAsync(vehicle.Organization.Id, date, true);

        var item = Assert.Single(result.InProgress.Items);
        Assert.Equal(new[] { "Полировка", "Мойка" }, item.Services);
        Assert.Equal(4, item.WorkTotalCount);
        Assert.True(item.HasTrackedWork);
        Assert.Equal(1, item.WorkCompletedCount);
        Assert.Equal(1, item.WorkPausedCount);
    }

    [Fact]
    public async Task WorkdaySeparatesUntrackedServicesFromHistoricalAssignments()
    {
        await using var context = fixture.CreateContext();
        var date = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);
        var vehicle = DetailingWorkdayQueryTests.CreateVehicle();
        var untracked = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.InProgress, date);
        var assigned = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.InProgress, date);
        var employee = new OrganizationEmployee { Organization = vehicle.Organization!, Name = "Сохранённый мастер" };
        assigned.EmployeeId = employee.Id;
        context.OrganizationEmployees.Add(employee);
        context.Maintenance.AddRange(untracked, assigned);
        await context.SaveChangesAsync();

        var result = await new OrganizationWorkdayQueryService(context).GetWorkdayAsync(vehicle.Organization!.Id, date, true);

        var untrackedItem = Assert.Single(result.InProgress.Items, item => item.Id == untracked.Id);
        Assert.Equal(2, untrackedItem.WorkTotalCount);
        Assert.False(untrackedItem.HasTrackedWork);
        Assert.True(Assert.Single(result.InProgress.Items, item => item.Id == assigned.Id).HasTrackedWork);
    }

    [Fact]
    public async Task WorkdayIncludesOldActiveWorkAndSeparatesCalendarDaysAndOrganizations()
    {
        await using var context = fixture.CreateContext();
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        var vehicle = DetailingWorkdayQueryTests.CreateVehicle();
        var plannedPast = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Planned, date.AddDays(-3));
        var plannedToday = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Planned, date);
        var active = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.InProgress, date.AddDays(-90));
        var completed = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Completed, date);
        completed.PlannedDate = date.AddDays(-5);
        completed.PlannedEndDate = date.AddDays(1);
        active.PlannedEndDate = date;
        completed.CompletedDate = date;
        completed.ReadyForHandoverAt = date;
        var deleted = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Planned, date);
        deleted.IsDeleted = true;
        var deletedVehicle = DetailingWorkdayQueryTests.CreateVehicle();
        deletedVehicle.Organization = vehicle.Organization;
        deletedVehicle.IsDeleted = true;
        context.Maintenance.AddRange(plannedPast, plannedToday, active, completed, deleted,
            DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Planned, date.AddDays(1)),
            DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Completed, date.AddDays(-1)),
            DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Cancelled, date),
            DetailingWorkdayQueryTests.CreateWork(DetailingWorkdayQueryTests.CreateVehicle(), MaintenanceStatus.InProgress, date),
            DetailingWorkdayQueryTests.CreateWork(deletedVehicle, MaintenanceStatus.InProgress, date));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = new OrganizationWorkspaceQueryService(
            new OrganizationWorkdayQueryService(context),
            new OrganizationWorkspaceMaintenanceQueryService(context),
            new OrganizationFinanceQueryService(context),
            new OrganizationMaintenanceJournalQueryService(context),
            new OrganizationWorkJournalQueryService(context),
            new OrganizationWorkspaceAnalyticsQueryService(context),
            new OrganizationWorkspacePlanningQueryService(context));

        var result = await service.GetWorkdayAsync(vehicle.Organization!.Id, date, true);

        Assert.True(result.HasAnyWork);
        Assert.Equal(new[] { plannedToday.Id, plannedPast.Id }, result.Planned.Items.Select(item => item.Id));
        Assert.Equal(active.Id, Assert.Single(result.InProgress.Items).Id);
        Assert.Equal(completed.Id, Assert.Single(result.Ready.Items).Id);
        Assert.Equal(date, result.InProgress.Items[0].EndDate);
        Assert.Equal(date.AddDays(1), result.Ready.Items[0].EndDate);
        Assert.Equal("Проверочный клиент", result.Planned.Items[0].ClientName);
        Assert.Equal("+79001234567", result.Planned.Items[0].ClientPhone);
        Assert.Equal(new[] { "Полировка", "Химчистка" }, result.Planned.Items[0].Services);
        Assert.Equal("BMW X3", result.Planned.Items[0].VehicleName);

        var restricted = await service.GetWorkdayAsync(vehicle.Organization.Id, date, false);
        Assert.All(restricted.Planned.Items, item => { Assert.Null(item.ClientName); Assert.Null(item.ClientPhone); });
        var empty = await service.GetWorkdayAsync(Guid.NewGuid(), date, false);
        Assert.False(empty.HasAnyWork);
        Assert.Empty(empty.Planned.Items);
    }

    [Theory]
    [InlineData(MaintenanceStatus.Planned)]
    [InlineData(MaintenanceStatus.InProgress)]
    [InlineData(MaintenanceStatus.Completed)]
    public async Task EachGroupReturnsNewestFirstBeforeApplyingLimit(MaintenanceStatus status)
    {
        await using var context = fixture.CreateContext();
        var date = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc);
        var vehicle = DetailingWorkdayQueryTests.CreateVehicle();
        var works = new List<MaintenanceEntity>();
        for (var index = 0; index < 55; index++)
        {
            var work = DetailingWorkdayQueryTests.CreateWork(vehicle, status, date);
            work.PlannedStartTime = index == 0 ? null : TimeSpan.FromMinutes(index * 15);
            work.ReadyForHandoverAt = status == MaintenanceStatus.Completed ? date.AddMinutes(index * 15) : null;
            works.Add(work);
        }
        context.Maintenance.AddRange(works);
        await context.SaveChangesAsync();
        var service = new OrganizationWorkspaceQueryService(
            new OrganizationWorkdayQueryService(context),
            new OrganizationWorkspaceMaintenanceQueryService(context),
            new OrganizationFinanceQueryService(context),
            new OrganizationMaintenanceJournalQueryService(context),
            new OrganizationWorkJournalQueryService(context),
            new OrganizationWorkspaceAnalyticsQueryService(context),
            new OrganizationWorkspacePlanningQueryService(context));

        var result = await service.GetWorkdayAsync(vehicle.Organization!.Id, date, true);

        var group = status switch
        {
            MaintenanceStatus.Planned => result.Planned,
            MaintenanceStatus.InProgress => result.InProgress,
            _ => result.Ready
        };

        Assert.Equal(55, group.TotalCount);
        Assert.Equal(works.AsEnumerable().Reverse().Take(50).Select(item => item.Id), group.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task HandoverUsesLocalDayAndReadyBacklogExcludesLegacyCompletedRecords()
    {
        await using var context = fixture.CreateContext();
        var date = new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc);
        var vehicle = DetailingWorkdayQueryTests.CreateVehicle();
        var ready = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Completed, date.AddDays(-5));
        ready.ReadyForHandoverAt = date.AddDays(-5);
        var first = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Completed, date.AddDays(-2));
        first.ReadyForHandoverAt = date.AddDays(-2);
        first.HandedOverAt = date.AddHours(-7);
        var last = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Completed, date.AddDays(-2));
        last.ReadyForHandoverAt = date.AddDays(-2);
        last.HandedOverAt = date.AddHours(17).AddTicks(-1);
        var nextDay = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Completed, date);
        nextDay.ReadyForHandoverAt = date;
        nextDay.HandedOverAt = date.AddHours(17);
        var previousDay = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Completed, date.AddDays(-2));
        previousDay.ReadyForHandoverAt = date.AddDays(-2);
        previousDay.HandedOverAt = date.AddHours(-7).AddTicks(-10);
        context.Maintenance.AddRange(ready, first, last, nextDay, previousDay,
            DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Completed, date));
        await context.SaveChangesAsync();
        var service = new OrganizationWorkspaceQueryService(
            new OrganizationWorkdayQueryService(context),
            new OrganizationWorkspaceMaintenanceQueryService(context),
            new OrganizationFinanceQueryService(context),
            new OrganizationMaintenanceJournalQueryService(context),
            new OrganizationWorkJournalQueryService(context),
            new OrganizationWorkspaceAnalyticsQueryService(context),
            new OrganizationWorkspacePlanningQueryService(context));

        var result = await service.GetWorkdayAsync(vehicle.Organization!.Id, date, false,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Novosibirsk"));

        Assert.Equal(ready.Id, Assert.Single(result.Ready.Items).Id);
        Assert.Equal(new[] { last.Id, first.Id }, result.HandedOver.Items.Select(item => item.Id));
        Assert.All(result.HandedOver.Items, item => Assert.Null(item.ClientPhone));
        var journal = await service.GetMaintenanceJournalAsync(vehicle.Organization.Id, MaintenanceStatus.Completed,
            null, null, null, 1, 25, false, handover: "ready");
        Assert.Equal("Готов к выдаче", Assert.Single(journal.Items).StatusName);
        Assert.Equal(1, journal.TotalCount);
        var issuedJournal = await service.GetMaintenanceJournalAsync(vehicle.Organization.Id, MaintenanceStatus.Completed,
            null, null, null, 1, 2, false, handover: "handedOver");
        Assert.Equal(4, issuedJournal.TotalCount);
        Assert.Equal(2, issuedJournal.Items.Count);
        Assert.All(issuedJournal.Items, item => Assert.Equal("Выдан", item.StatusName));
    }

    [Fact]
    public async Task MaintenanceJournalMarksKnownZeroTotalAndHidesPaymentStatusWithoutPermission()
    {
        await using var context = fixture.CreateContext();
        var date = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);
        var vehicle = DetailingWorkdayQueryTests.CreateVehicle();
        var maintenance = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Completed, date);
        maintenance.DiscountAmount = 50m;
        maintenance.Details = [new() { Name = "Полировка", Price = 50m }];
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        var service = new OrganizationMaintenanceJournalQueryService(context);

        var financeVisible = await service.GetMaintenanceJournalAsync(vehicle.Organization!.Id, MaintenanceStatus.Completed,
            null, null, null, 1, 25, false, canViewPayments: true);
        var item = Assert.Single(financeVisible.Items);
        Assert.Equal(0m, item.Amount);
        Assert.Equal(0m, item.PaidAmount);
        Assert.Equal(MaintenancePaymentStatus.NoPaymentRequired, item.PaymentStatus);

        var financeHidden = await service.GetMaintenanceJournalAsync(vehicle.Organization.Id, MaintenanceStatus.Completed,
            null, null, null, 1, 25, false);
        var hiddenItem = Assert.Single(financeHidden.Items);
        Assert.Null(hiddenItem.PaidAmount);
        Assert.Null(hiddenItem.PaymentStatus);
    }

    [Fact]
    public async Task VisualizationIncludesAllActiveOrdersAndExcludesHandedOverAndOtherOrganizations()
    {
        await using var context = fixture.CreateContext();
        var date = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);
        var vehicle = DetailingWorkdayQueryTests.CreateVehicle();
        for (var index = 0; index < 55; index++)
        {
            context.Maintenance.Add(DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.InProgress, date.AddDays(-2)));
            context.Maintenance.Add(DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Planned, date));
        }

        var ready = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Completed, date);
        ready.ReadyForHandoverAt = date;
        var handedOver = DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Completed, date);
        handedOver.ReadyForHandoverAt = date;
        handedOver.HandedOverAt = date.AddHours(1);
        context.Maintenance.AddRange(ready, handedOver,
            DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Cancelled, date),
            DetailingWorkdayQueryTests.CreateWork(vehicle, MaintenanceStatus.Planned, date.AddDays(1)),
            DetailingWorkdayQueryTests.CreateWork(DetailingWorkdayQueryTests.CreateVehicle(), MaintenanceStatus.InProgress, date));

        await context.SaveChangesAsync();
        var service = new OrganizationWorkspaceQueryService(
            new OrganizationWorkdayQueryService(context),
            new OrganizationWorkspaceMaintenanceQueryService(context),
            new OrganizationFinanceQueryService(context),
            new OrganizationMaintenanceJournalQueryService(context),
            new OrganizationWorkJournalQueryService(context),
            new OrganizationWorkspaceAnalyticsQueryService(context),
            new OrganizationWorkspacePlanningQueryService(context));

        var result = await service.GetVisualizationAsync(vehicle.Organization!.Id, date, false);

        Assert.Equal(55, result.InProgress.TotalCount);
        Assert.Equal(55, result.Planned.TotalCount);
        Assert.Equal(1, result.Ready.TotalCount);
        Assert.Equal(55, result.InProgress.Items.Count);
        Assert.Equal(55, result.Planned.Items.Count);
        Assert.Equal(ready.Id, Assert.Single(result.Ready.Items).Id);
        Assert.Empty(result.HandedOver.Items);
        Assert.All(result.InProgress.Items, item => Assert.Null(item.ClientPhone));
    }

    private static Vehicle CreateVehicle()
    {
        var user = new ApplicationUser();
        var organization = new Organization
        {
            Name = "Проверка рабочего дня",
            PrimaryScenarioCode = OrganizationTypeCodes.Detailing,
            OwnerUser = user,
            Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
        };

        return new Vehicle
        {
            User = user,
            Organization = organization,
            ManualBrandName = "BMW",
            ManualModelName = "X3",
            RegistrationPlate = "А123АА154",
            OrganizationClient = new OrganizationClient { Organization = organization, Name = "Проверочный клиент", Phone = "+79001234567" }
        };
    }

    private static MaintenanceEntity CreateWork(Vehicle vehicle, MaintenanceStatus status, DateTime date)
    {
        return new MaintenanceEntity
        {
            Vehicle = vehicle,
            User = vehicle.User,
            Type = MaintenanceType.Other,
            Status = status,
            Date = date,
            PlannedDate = date,
            DateTimeCreation = date,
            Details = [new() { Name = "Полировка" }, new() { Name = "Химчистка" }, new() { Name = "Материал", MaterialName = "Керамика" }]
        };
    }
}
