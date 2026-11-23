using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationShiftTransactionTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task CopyKeepsLocalTimeAndBreakAndRepeatedCopyCreatesNoDuplicate()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        const string timeZoneId = "Asia/Novosibirsk";
        organization.ShiftTimeZoneId = timeZoneId;
        var employee = new OrganizationEmployee { Organization = organization, Name = "Мастер" };
        var targetMonday = new DateOnly(2027, 1, 4);
        var sourceDate = targetMonday.AddDays(-7);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var sourceStartLocal = DateTime.SpecifyKind(sourceDate.ToDateTime(new TimeOnly(9, 0)), DateTimeKind.Unspecified);
        var sourceEndLocal = DateTime.SpecifyKind(sourceDate.ToDateTime(new TimeOnly(18, 0)), DateTimeKind.Unspecified);
        context.OrganizationEmployees.Add(employee);
        context.OrganizationEmployeeShifts.Add(new OrganizationEmployeeShift
        {
            Organization = organization,
            Employee = employee,
            LocalStartDate = sourceDate,
            PlanStartUtc = TimeZoneInfo.ConvertTimeToUtc(sourceStartLocal, zone),
            PlanEndUtc = TimeZoneInfo.ConvertTimeToUtc(sourceEndLocal, zone),
            PlannedBreakMinutes = 45,
            TimeZoneId = timeZoneId,
            IsPublished = true
        });
        await context.SaveChangesAsync();
        var service = this.CreateService(context);

        Assert.Equal(1, await service.CopyPreviousWeekAsync(organization.OwnerUserId, organization.Id, targetMonday));
        var copied = Assert.Single((await service.GetWeekAsync(organization.OwnerUserId, organization.Id, targetMonday)).Shifts,
            shift => shift.LocalStartDate == targetMonday);
        Assert.Equal("2027-01-04T09:00", copied.StartLocal);
        Assert.Equal("2027-01-04T18:00", copied.EndLocal);
        Assert.Equal(45, copied.PlannedBreakMinutes);
        Assert.Equal("Asia/Novosibirsk", copied.TimeZoneId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CopyPreviousWeekAsync(organization.OwnerUserId, organization.Id, targetMonday));

        await using var verification = fixture.CreateContext();
        Assert.Equal(1, await verification.OrganizationEmployeeShifts.CountAsync(item =>
            item.OrganizationId == organization.Id && item.LocalStartDate == targetMonday));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopyRetryBeforeOrAfterCommitKeepsOneCopyAndAuditPerSource(bool afterCommit)
    {
        var failure = new OrganizationCommitFailureInterceptor(afterCommit);
        await using var context = fixture.CreateContext(failure);
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.ShiftTimeZoneId = "Etc/UTC";
        var employee = new OrganizationEmployee { Organization = organization, Name = "Мастер" };
        var monday = new DateOnly(2027, 1, 11);
        context.OrganizationEmployees.Add(employee);
        context.OrganizationEmployeeShifts.Add(this.CreateShift(organization, employee, monday.AddDays(-7), 9));
        context.OrganizationEmployeeShifts.Add(this.CreateShift(organization, employee, monday.AddDays(-6), 10));
        await context.SaveChangesAsync();
        failure.Arm();

        var copied = await this.CreateService(context).CopyPreviousWeekAsync(organization.OwnerUserId, organization.Id, monday);

        Assert.Equal(2, copied);
        Assert.Equal(1, failure.Failures);
        await using var verification = fixture.CreateContext();
        Assert.Equal(2, await verification.OrganizationEmployeeShifts.CountAsync(item =>
            item.OrganizationId == organization.Id && item.LocalStartDate >= monday));
        Assert.Equal(2, await verification.OrganizationAuditEvents.CountAsync(item =>
            item.OrganizationId == organization.Id && item.EventType == "shifts.copied"));
    }

    [Fact]
    public async Task ConflictingCopyRollsBackTheWholeWeek()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.ShiftTimeZoneId = "Etc/UTC";
        var first = new OrganizationEmployee { Organization = organization, Name = "Первый мастер" };
        var second = new OrganizationEmployee { Organization = organization, Name = "Второй мастер" };
        var monday = new DateOnly(2027, 1, 11);
        context.OrganizationEmployees.AddRange(first, second);
        context.OrganizationEmployeeShifts.AddRange(
            this.CreateShift(organization, first, monday.AddDays(-7), 9),
            this.CreateShift(organization, second, monday.AddDays(-7), 10),
            this.CreateShift(organization, second, monday, 10));
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            this.CreateService(context).CopyPreviousWeekAsync(organization.OwnerUserId, organization.Id, monday));

        await using var verification = fixture.CreateContext();
        Assert.Equal(1, await verification.OrganizationEmployeeShifts.CountAsync(item =>
            item.OrganizationId == organization.Id && item.LocalStartDate >= monday));
        Assert.False(await verification.OrganizationAuditEvents.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task DeniedScheduleWriteDoesNotPersistShiftOrAudit()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.ShiftTimeZoneId = "Etc/UTC";
        var employee = new OrganizationEmployee { Organization = organization, Name = "Мастер" };
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        var subscriptions = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        subscriptions.Setup(value => value.EnsureWorkspaceCanWriteAsync(
                organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.ShiftsManage))
            .ThrowsAsync(new UnauthorizedAccessException("Нет доступа."));
        var service = new OrganizationShiftService(context, subscriptions.Object,
            Mock.Of<IOrganizationPermissionService>(), new OrganizationAuditService(context));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(organization.OwnerUserId,
            organization.Id, null, new SaveOrganizationShiftDto
            {
                EmployeeId = employee.Id, StartLocal = new DateTime(2027, 1, 11, 9, 0, 0),
                EndLocal = new DateTime(2027, 1, 11, 17, 0, 0)
            }));

        Assert.False(await context.OrganizationEmployeeShifts.AnyAsync(item => item.OrganizationId == organization.Id));
        Assert.False(await context.OrganizationAuditEvents.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    private OrganizationShiftService CreateService(OpenCRM.Infrastructure.Data.OpenCRMContext context) =>
        new(context, Mock.Of<IOrganizationSubscriptionService>(), Mock.Of<IOrganizationPermissionService>(),
            new OrganizationAuditService(context));

    private OrganizationEmployeeShift CreateShift(Organization organization, OrganizationEmployee employee, DateOnly day, int hour)
    {
        var start = DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(hour, 0)), DateTimeKind.Utc);

        return new OrganizationEmployeeShift
        {
            OrganizationId = organization.Id, EmployeeId = employee.Id, LocalStartDate = day,
            PlanStartUtc = start, PlanEndUtc = start.AddHours(8), TimeZoneId = "Etc/UTC", IsPublished = true
        };
    }
}
