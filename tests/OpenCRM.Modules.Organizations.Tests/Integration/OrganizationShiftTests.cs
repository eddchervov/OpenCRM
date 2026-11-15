using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Organizations;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationShiftTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task SavedShiftIsPublishedViewedAndCancelledWithoutOverlapping()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Email = "shift-owner@example.test" };
        var performer = new ApplicationUser { Email = "shift-performer@example.test" };
        var organization = new Organization
        {
            Name = "График смен",
            OwnerUser = owner,
            ShiftTimeZoneId = "Etc/UTC",
            Members =
            [
                new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner },
                new OrganizationMember { User = performer, RoleCode = OrganizationRoleCodes.Performer }
            ]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var employee = new OrganizationEmployee
        {
            OrganizationId = organization.Id,
            Name = "Исполнитель",
            IsActive = true,
            MemberId = organization.Members.Single(member => member.UserId == performer.Id).Id
        };
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();

        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(value => value.EnsureWorkspaceCanReadAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscription.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(value => value.HasPermissionAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        var audit = new Mock<IOrganizationAuditService>();
        audit.Setup(value => value.AddAsync(It.IsAny<OrganizationAuditEventCreateDto>(), false))
            .Returns(Task.CompletedTask);
        var service = new OrganizationShiftService(context, subscription.Object, permissions.Object, audit.Object);
        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var request = new SaveOrganizationShiftDto
        {
            EmployeeId = employee.Id,
            StartLocal = startDate.ToDateTime(new TimeOnly(9, 0)),
            EndLocal = startDate.ToDateTime(new TimeOnly(18, 0)),
            PlannedBreakMinutes = 60
        };

        var shiftId = await service.SaveAsync(owner.Id, organization.Id, null, request);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(owner.Id, organization.Id, null, request));
        var weekStart = startDate.AddDays(-((int)startDate.DayOfWeek + 6) % 7);
        Assert.Equal(0, await service.PublishWeekAsync(owner.Id, organization.Id, weekStart));
        var ownShift = Assert.Single(await service.GetOwnAsync(performer.Id, organization.Id));
        Assert.Equal(shiftId, ownShift.Id);
        Assert.True(ownShift.IsPublished);

        var plannedDay = DateTime.SpecifyKind(startDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var vehicle = new Vehicle
        {
            User = owner,
            Organization = organization,
            RegistrationPlate = "А123АА154"
        };
        context.Maintenance.Add(new OpenCRM.Domain.Maintenance
        {
            User = owner,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Planned,
            DateTimeCreation = DateTime.UtcNow,
            Date = plannedDay,
            PlannedDate = plannedDay,
            PlannedEndDate = plannedDay,
            PlannedStartTime = TimeSpan.FromHours(9),
            PlannedEndTime = TimeSpan.FromHours(21),
            Details = [new MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Работа", EmployeeId = employee.Id }]
        });
        await context.SaveChangesAsync();
        var assignedWeek = await service.GetWeekAsync(owner.Id, organization.Id, weekStart);
        var assignedMaintenance = Assert.Single(assignedWeek.Assignments);
        Assert.Equal(employee.Id, assignedMaintenance.EmployeeId);
        Assert.Equal("09:00", assignedMaintenance.Segments[0].StartLocal[11..]);
        Assert.False(assignedMaintenance.Segments[0].IsOutsideShift);
        Assert.Equal("18:00", assignedMaintenance.Segments[1].StartLocal[11..]);
        Assert.Equal("21:00", assignedMaintenance.Segments[1].EndLocal[11..]);
        Assert.True(assignedMaintenance.Segments[1].IsOutsideShift);

        var recordedMaintenance = await context.Maintenance.SingleAsync(item =>
            item.VehicleId == vehicle.Id && item.PlannedDate == plannedDay);
        recordedMaintenance.PlannedStartTime = TimeSpan.FromHours(10);
        await context.SaveChangesAsync();
        var updatedAssignment = Assert.Single((await service.GetWeekAsync(owner.Id, organization.Id, weekStart)).Assignments);
        Assert.Equal("10:00", updatedAssignment.Segments[0].StartLocal[11..]);

        recordedMaintenance.IsDeleted = true;
        await context.SaveChangesAsync();
        Assert.Empty((await service.GetWeekAsync(owner.Id, organization.Id, weekStart)).Assignments);

        var sunday = weekStart.AddDays(-1);
        var overnightShift = new SaveOrganizationShiftDto
        {
            EmployeeId = employee.Id,
            StartLocal = sunday.ToDateTime(new TimeOnly(22, 0)),
            EndLocal = weekStart.ToDateTime(new TimeOnly(2, 0))
        };
        var overnightShiftId = await service.SaveAsync(owner.Id, organization.Id, null, overnightShift);
        var sundayDate = DateTime.SpecifyKind(sunday.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var mondayDate = DateTime.SpecifyKind(weekStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var overnightMaintenance = new OpenCRM.Domain.Maintenance
        {
            User = owner,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Planned,
            DateTimeCreation = DateTime.UtcNow,
            Date = sundayDate,
            PlannedDate = sundayDate,
            PlannedEndDate = mondayDate,
            PlannedStartTime = TimeSpan.FromHours(23),
            PlannedEndTime = TimeSpan.FromHours(1),
            Details = [new MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Ночная работа", EmployeeId = employee.Id }]
        };
        context.Maintenance.Add(overnightMaintenance);
        await context.SaveChangesAsync();
        var boundaryWeek = await service.GetWeekAsync(owner.Id, organization.Id, weekStart);
        Assert.Contains(boundaryWeek.Shifts, shift => shift.Id == overnightShiftId && shift.IsPublished);
        var mondayAssignment = Assert.Single(boundaryWeek.Assignments.Where(item =>
            item.MaintenanceId == overnightMaintenance.Id && item.LocalStartDate == weekStart));
        Assert.Equal("00:00", Assert.Single(mondayAssignment.Segments).StartLocal[11..]);
        Assert.False(mondayAssignment.Segments[0].IsOutsideShift);

        await service.SetTimeZoneAsync(owner.Id, organization.Id, "Europe/Moscow");
        var shiftedWeek = await service.GetWeekAsync(owner.Id, organization.Id, weekStart);
        var shiftedOvernight = Assert.Single(shiftedWeek.Shifts.Where(shift => shift.Id == overnightShiftId));
        Assert.Equal(weekStart, shiftedOvernight.LocalStartDate);
        Assert.Equal("01:00", shiftedOvernight.StartLocal[11..]);
        Assert.Equal("12:00", Assert.Single(shiftedWeek.Shifts.Where(shift => shift.Id == shiftId)).StartLocal[11..]);
        Assert.Contains(await service.GetOwnAsync(performer.Id, organization.Id), shift =>
            shift.Id == shiftId && shift.StartLocal[11..] == "12:00");
        Assert.Equal(0, await service.PublishWeekAsync(owner.Id, organization.Id, weekStart));
        var publishedOvernight = Assert.Single((await service.GetWeekAsync(owner.Id, organization.Id, weekStart)).Shifts
            .Where(shift => shift.Id == overnightShiftId));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.SaveAsync(
            owner.Id, organization.Id, overnightShiftId, new SaveOrganizationShiftDto
            {
                EmployeeId = employee.Id,
                StartLocal = weekStart.ToDateTime(new TimeOnly(2, 0)),
                EndLocal = weekStart.ToDateTime(new TimeOnly(6, 0)),
                Version = publishedOvernight.Version + 1,
                Reason = "Устаревшая версия"
            }));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.CancelAsync(
            owner.Id, organization.Id, overnightShiftId, publishedOvernight.Version + 1, "Устаревшая версия"));

        await service.SaveAsync(owner.Id, organization.Id, overnightShiftId, new SaveOrganizationShiftDto
        {
            EmployeeId = employee.Id,
            StartLocal = weekStart.ToDateTime(new TimeOnly(2, 0)),
            EndLocal = weekStart.ToDateTime(new TimeOnly(6, 0)),
            Version = publishedOvernight.Version,
            Reason = "Уточнили время"
        });
        var editedShift = Assert.Single((await service.GetWeekAsync(owner.Id, organization.Id, weekStart)).Shifts
            .Where(shift => shift.Id == overnightShiftId));
        Assert.Equal("Europe/Moscow", editedShift.TimeZoneId);
        Assert.Equal("02:00", editedShift.StartLocal[11..]);
        permissions.Setup(value => value.HasPermissionAsync(
                owner.Id, organization.Id, OrganizationPermissionCodes.MaintenanceView))
            .ReturnsAsync(false);
        Assert.Empty((await service.GetWeekAsync(owner.Id, organization.Id, weekStart)).Assignments);
        employee.IsActive = false;
        await context.SaveChangesAsync();
        var week = await service.GetWeekAsync(owner.Id, organization.Id, weekStart);
        Assert.False(Assert.Single(week.Employees).IsActive);
        Assert.Contains(week.Shifts, shift => shift.Id == shiftId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CancelAsync(owner.Id, organization.Id, shiftId, ownShift.Version, null));
        await service.CancelAsync(owner.Id, organization.Id, shiftId, ownShift.Version, "Изменился график");
        Assert.Empty(await service.GetOwnAsync(performer.Id, organization.Id));

        subscription.Verify(value => value.EnsureWorkspaceCanWriteAsync(
            owner.Id, organization.Id, OrganizationPermissionCodes.ShiftsManage), Times.AtLeast(4));
        subscription.Verify(value => value.EnsureWorkspaceCanReadAsync(
            performer.Id, organization.Id, OrganizationPermissionCodes.ShiftsSelfView), Times.AtLeastOnce);

        var deniedSubscription = new Mock<IOrganizationSubscriptionService>();
        deniedSubscription.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), OrganizationPermissionCodes.ShiftsManage))
            .ThrowsAsync(new UnauthorizedAccessException());
        var deniedService = new OrganizationShiftService(context, deniedSubscription.Object, permissions.Object, audit.Object);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            deniedService.PublishWeekAsync(owner.Id, organization.Id, weekStart));

        var rolePermissions = new OrganizationPermissionService(context, audit.Object);
        var invalidRoleRights = new UpdateOrganizationRolePermissionsDto
        {
            RoleCode = OrganizationRoleCodes.Performer,
            EnabledPermissionCodes = [OrganizationPermissionCodes.WorkspaceView, OrganizationPermissionCodes.ShiftsManage]
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rolePermissions.UpdateRolePermissionsAsync(owner.Id, organization.Id, invalidRoleRights));
    }

    [Fact]
    public async Task PerformerCanViewOwnShiftButCannotManageSchedule()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser { Email = "shift-permission-owner@example.test" };
        var performer = new ApplicationUser { Email = "shift-permission-performer@example.test" };
        var organization = new Organization
        {
            Name = "Права на смены",
            OwnerUser = owner,
            ShiftTimeZoneId = "Etc/UTC",
            Members =
            [
                new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner },
                new OrganizationMember { User = performer, RoleCode = OrganizationRoleCodes.Performer }
            ]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var employee = new OrganizationEmployee
        {
            OrganizationId = organization.Id,
            Name = "Исполнитель",
            IsActive = true,
            MemberId = organization.Members.Single(member => member.UserId == performer.Id).Id
        };
        var shiftDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var weekStart = shiftDate.AddDays(-((int)shiftDate.DayOfWeek + 6) % 7);
        var startUtc = DateTime.SpecifyKind(shiftDate.ToDateTime(new TimeOnly(9, 0)), DateTimeKind.Utc);
        var shift = new OrganizationEmployeeShift
        {
            OrganizationId = organization.Id,
            Employee = employee,
            LocalStartDate = shiftDate,
            PlanStartUtc = startUtc,
            PlanEndUtc = startUtc.AddHours(8),
            TimeZoneId = "Etc/UTC",
            IsPublished = true
        };
        context.OrganizationEmployeeShifts.Add(shift);
        await context.SaveChangesAsync();

        var subscriptions = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        subscriptions.Setup(value => value.EnsureWorkspaceCanReadAsync(
                performer.Id, organization.Id, OrganizationPermissionCodes.ShiftsView))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(value => value.EnsureWorkspaceCanReadAsync(
                performer.Id, organization.Id, OrganizationPermissionCodes.ShiftsSelfView))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                performer.Id, organization.Id, OrganizationPermissionCodes.ShiftsManage))
            .ThrowsAsync(new UnauthorizedAccessException());
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(value => value.HasPermissionAsync(
                performer.Id, organization.Id, OrganizationPermissionCodes.ShiftsManage))
            .ReturnsAsync(false);
        permissions.Setup(value => value.HasPermissionAsync(
                performer.Id, organization.Id, OrganizationPermissionCodes.MaintenanceView))
            .ReturnsAsync(false);
        var service = new OrganizationShiftService(context, subscriptions.Object,
            permissions.Object, new OrganizationAuditService(context));

        var week = await service.GetWeekAsync(performer.Id, organization.Id, weekStart);
        Assert.False(week.CanManage);
        Assert.Contains(week.Shifts, item => item.Id == shift.Id && item.IsPublished);
        Assert.Contains(await service.GetOwnAsync(performer.Id, organization.Id), item => item.Id == shift.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(
            performer.Id, organization.Id, shift.Id, new SaveOrganizationShiftDto
            {
                EmployeeId = employee.Id,
                StartLocal = shiftDate.ToDateTime(new TimeOnly(10, 0)),
                EndLocal = shiftDate.ToDateTime(new TimeOnly(18, 0)),
                Version = shift.Version,
                Reason = "Изменение исполнителем"
            }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CancelAsync(
            performer.Id, organization.Id, shift.Id, shift.Version, "Отмена исполнителем"));
    }

}
