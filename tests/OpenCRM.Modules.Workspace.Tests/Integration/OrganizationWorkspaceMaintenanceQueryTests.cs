using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Workspace.Queries;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Testing;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class OrganizationWorkspaceMaintenanceQueryTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Fact]
    public async Task MaintenanceFeedNormalizesPeriodFiltersScheduledWorkAndClampsPage()
    {
        await using var context = fixture.CreateContext();
        var (user, organization, vehicle) = CreateVehicle("Workspace maintenance feed");
        var date = UtcDate(2026, 10, 13);
        var inProgress = CreateWork(user, vehicle, MaintenanceStatus.InProgress, date.AddDays(-10));
        var openEnded = CreateWork(user, vehicle, MaintenanceStatus.Planned, date);
        openEnded.PlannedEndDate = date;
        openEnded.PlannedEndTime = null;
        var positiveEndTime = CreateWork(user, vehicle, MaintenanceStatus.Planned, date);
        positiveEndTime.PlannedEndDate = date;
        positiveEndTime.PlannedEndTime = TimeSpan.FromMinutes(1);
        var endsTomorrow = CreateWork(user, vehicle, MaintenanceStatus.Planned, date);
        endsTomorrow.PlannedEndDate = date.AddDays(1);
        var zeroEndTime = CreateWork(user, vehicle, MaintenanceStatus.Planned, date);
        zeroEndTime.PlannedEndDate = date;
        zeroEndTime.PlannedEndTime = TimeSpan.Zero;
        var startsOutside = CreateWork(user, vehicle, MaintenanceStatus.Planned, date.AddDays(1));
        var completed = CreateWork(user, vehicle, MaintenanceStatus.Completed, date);
        context.Users.Add(user);
        context.Organizations.Add(organization);
        context.Maintenance.AddRange(inProgress, openEnded, positiveEndTime, endsTomorrow, zeroEndTime, startsOutside, completed);
        await context.SaveChangesAsync();
        var service = new OrganizationWorkspaceMaintenanceQueryService(context);

        var firstPage = await service.GetMaintenancesAsync(organization.Id, date, date, 1, 2);
        var lastPage = await service.GetMaintenancesAsync(organization.Id, date, date, int.MaxValue, 2);
        var normalizedPeriod = await service.GetMaintenancesAsync(organization.Id, date.AddDays(1), date, 1, 25);

        Assert.Equal(4, firstPage.TotalCount);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(2, firstPage.PageSize);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(2, lastPage.Page);
        Assert.Equal(2, lastPage.Items.Count);
        Assert.Equal(5, normalizedPeriod.TotalCount);
        Assert.Contains(inProgress.Id, firstPage.Items.Select(item => item.Id));
        Assert.DoesNotContain(zeroEndTime.Id, firstPage.Items.Select(item => item.Id));
        Assert.DoesNotContain(completed.Id, normalizedPeriod.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task MaintenanceCalendarSupportsStatusModeAndVehicleFiltering()
    {
        await using var context = fixture.CreateContext();
        var (user, organization, vehicle) = CreateVehicle("Workspace maintenance calendar");
        var otherVehicle = CreateVehicleForOrganization(user, organization);
        var date = UtcDate(2026, 10, 13);
        var inProgress = CreateWork(user, vehicle, MaintenanceStatus.InProgress, date.AddDays(-10));
        var plannedOpenEnded = CreateWork(user, vehicle, MaintenanceStatus.Planned, date);
        plannedOpenEnded.PlannedEndDate = date;
        var plannedZeroEndTime = CreateWork(user, vehicle, MaintenanceStatus.Planned, date);
        plannedZeroEndTime.PlannedEndDate = date;
        plannedZeroEndTime.PlannedEndTime = TimeSpan.Zero;
        var fallbackDate = CreateWork(user, vehicle, MaintenanceStatus.Planned, date);
        fallbackDate.PlannedDate = null;
        fallbackDate.PlannedEndDate = null;
        var completed = CreateWork(user, vehicle, MaintenanceStatus.Completed, date);
        var cancelled = CreateWork(user, otherVehicle, MaintenanceStatus.Cancelled, date);
        var deleted = CreateWork(user, otherVehicle, MaintenanceStatus.Planned, date);
        deleted.IsDeleted = true;
        context.Users.Add(user);
        context.Organizations.Add(organization);
        context.Maintenance.AddRange(inProgress, plannedOpenEnded, plannedZeroEndTime, fallbackDate, completed, cancelled, deleted);
        await context.SaveChangesAsync();
        var service = new OrganizationWorkspaceMaintenanceQueryService(context);

        var activeCalendar = await service.GetMaintenanceCalendarAsync(organization.Id, date, date);
        var allStatuses = await service.GetMaintenanceCalendarAsync(
            organization.Id,
            date.AddDays(1),
            date,
            vehicle.Id,
            includeAllStatuses: true);
        var otherVehicleCalendar = await service.GetMaintenanceCalendarAsync(
            organization.Id,
            date,
            date,
            otherVehicle.Id,
            includeAllStatuses: true);

        Assert.Contains(inProgress.Id, activeCalendar.Select(item => item.Id));
        Assert.Contains(plannedOpenEnded.Id, activeCalendar.Select(item => item.Id));
        Assert.Contains(fallbackDate.Id, activeCalendar.Select(item => item.Id));
        Assert.Contains(completed.Id, allStatuses.Select(item => item.Id));
        Assert.DoesNotContain(plannedZeroEndTime.Id, allStatuses.Select(item => item.Id));
        Assert.DoesNotContain(cancelled.Id, allStatuses.Select(item => item.Id));
        Assert.Equal(new[] { cancelled.Id }, otherVehicleCalendar.Select(item => item.Id));
    }

    private static (ApplicationUser User, Organization Organization, Vehicle Vehicle) CreateVehicle(string name)
    {
        var user = new ApplicationUser();
        var organization = new Organization { Name = name, OwnerUser = user };
        var vehicle = CreateVehicleForOrganization(user, organization);

        return (user, organization, vehicle);
    }

    private static Vehicle CreateVehicleForOrganization(ApplicationUser user, Organization organization) =>
        new()
        {
            User = user,
            Organization = organization,
            Name = "Тестовый автомобиль",
            ManualBrandName = "BMW",
            ManualModelName = "X5",
            RegistrationPlate = "А123АА154"
        };

    private static MaintenanceEntity CreateWork(
        ApplicationUser user,
        Vehicle vehicle,
        MaintenanceStatus status,
        DateTime date) =>
        new()
        {
            User = user,
            Vehicle = vehicle,
            Status = status,
            Type = MaintenanceType.Other,
            Date = date,
            PlannedDate = date,
            DateTimeCreation = date,
            Details = [new() { Name = "Работа" }]
        };

    private static DateTime UtcDate(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
}
