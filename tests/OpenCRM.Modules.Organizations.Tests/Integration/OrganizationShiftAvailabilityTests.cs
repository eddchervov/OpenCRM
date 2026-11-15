using Moq;
using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationShiftAvailabilityTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task AvailabilityUsesOnlyPublishedUncancelledShiftsOfTheRequestedEmployee()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.ShiftTimeZoneId = "Etc/UTC";
        var employee = new OrganizationEmployee { Organization = organization, Name = "Мастер" };
        var anotherEmployee = new OrganizationEmployee { Organization = organization, Name = "Другой мастер" };
        context.OrganizationEmployees.AddRange(employee, anotherEmployee);
        context.OrganizationEmployeeShifts.AddRange(
            OrganizationShiftAvailabilityTests.CreateShift(organization, employee, 9, 12),
            OrganizationShiftAvailabilityTests.CreateShift(organization, employee, 12, 15),
            OrganizationShiftAvailabilityTests.CreateShift(organization, employee, 15, 16, isPublished: false),
            OrganizationShiftAvailabilityTests.CreateShift(organization, employee, 16, 17, isCancelled: true),
            OrganizationShiftAvailabilityTests.CreateShift(organization, anotherEmployee, 15, 18));
        await context.SaveChangesAsync();
        var service = new OrganizationShiftService(context, Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<IOrganizationPermissionService>(), new OrganizationAuditService(context));

        var covered = await service.GetAvailabilityAsync(organization.OwnerUserId, organization.Id,
            employee.Id, "2027-01-11T09:00", "2027-01-11T15:00");
        var uncovered = await service.GetAvailabilityAsync(organization.OwnerUserId, organization.Id,
            employee.Id, "2027-01-11T09:00", "2027-01-11T17:00");

        Assert.True(covered.IsAvailable);
        Assert.False(uncovered.IsAvailable);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAvailabilityAsync(
            foreignOrganization.OwnerUserId, foreignOrganization.Id, employee.Id,
            "2027-01-11T09:00", "2027-01-11T15:00"));
    }

    [Fact]
    public async Task AvailabilityIsUnknownWithoutTimeZoneOrForAmbiguousLocalTime()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.ShiftTimeZoneId = null;
        var employee = new OrganizationEmployee { Organization = organization, Name = "Мастер" };
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        var service = new OrganizationShiftService(context, Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<IOrganizationPermissionService>(), new OrganizationAuditService(context));

        var withoutTimeZone = await service.GetAvailabilityAsync(organization.OwnerUserId, organization.Id,
            employee.Id, "2027-01-11T09:00", "2027-01-11T15:00");
        Assert.Null(withoutTimeZone.IsAvailable);
        organization.ShiftTimeZoneId = "Europe/Berlin";
        await context.SaveChangesAsync();
        var ambiguous = await service.GetAvailabilityAsync(organization.OwnerUserId, organization.Id,
            employee.Id, "2027-10-31T02:30", "2027-10-31T03:30");

        Assert.Null(ambiguous.IsAvailable);
    }

    private static OrganizationEmployeeShift CreateShift(
        Organization organization,
        OrganizationEmployee employee,
        int startHour,
        int endHour,
        bool isPublished = true,
        bool isCancelled = false) => new()
        {
            Organization = organization,
            Employee = employee,
            LocalStartDate = new DateOnly(2027, 1, 11),
            PlanStartUtc = new DateTime(2027, 1, 11, startHour, 0, 0, DateTimeKind.Utc),
            PlanEndUtc = new DateTime(2027, 1, 11, endHour, 0, 0, DateTimeKind.Utc),
            TimeZoneId = "Etc/UTC",
            IsPublished = isPublished,
            IsCancelled = isCancelled
        };
}
