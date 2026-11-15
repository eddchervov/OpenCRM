using OpenCRM.Domain;
using OpenCRM.Modules.Organizations.Shifts;

namespace OpenCRM.Modules.Organizations.Tests.Unit;

public sealed class OrganizationShiftRulesTests
{
    [Theory]
    [InlineData(2027, 3, 28)]
    [InlineData(2027, 10, 31)]
    public void InvalidOrAmbiguousLocalTimeCannotBeAssignedToShift(int year, int month, int day)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var time = new DateTime(year, month, day, 2, 30, 0, DateTimeKind.Unspecified);

        Assert.Throws<InvalidOperationException>(() => OrganizationShiftRules.ValidateLocalTime(time, zone));
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    public void LocalTimeInputMustNotAlreadyCarryTimeZone(DateTimeKind kind)
    {
        var time = new DateTime(2027, 1, 4, 9, 0, 0, kind);

        Assert.Throws<InvalidOperationException>(() => OrganizationShiftRules.ValidateLocalTime(time, TimeZoneInfo.Utc));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(-30, true)]
    public void CoverageAllowsTouchingOrOverlappingShiftsButRejectsGap(int gapMinutes, bool expected)
    {
        var start = new DateTime(2027, 1, 4, 9, 0, 0, DateTimeKind.Utc);
        var shifts = new[]
        {
            new OrganizationEmployeeShift { PlanStartUtc = start.AddHours(3).AddMinutes(gapMinutes), PlanEndUtc = start.AddHours(8) },
            new OrganizationEmployeeShift { PlanStartUtc = start, PlanEndUtc = start.AddHours(3) }
        };

        Assert.Equal(expected, OrganizationShiftRules.IsCoveredByShifts(shifts, start, start.AddHours(8)));
    }

    [Fact]
    public void OvernightAssignmentIsClippedToRequestedDayAndKeepsOutsideShiftMarkers()
    {
        var employeeId = Guid.NewGuid();
        var day = new DateOnly(2027, 1, 4);
        var start = DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(23, 0)), DateTimeKind.Utc);
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            PlannedDate = day.ToDateTime(TimeOnly.MinValue), PlannedEndDate = day.AddDays(1).ToDateTime(TimeOnly.MinValue),
            PlannedStartTime = TimeSpan.FromHours(23), PlannedEndTime = TimeSpan.FromHours(2)
        };
        var shifts = new[]
        {
            new OrganizationEmployeeShift
            {
                EmployeeId = employeeId, IsPublished = true,
                PlanStartUtc = start.AddHours(1), PlanEndUtc = start.AddHours(2)
            }
        };

        var segments = OrganizationShiftRules.BuildAssignmentSegments(maintenance, employeeId, TimeZoneInfo.Utc, shifts);
        var nextDay = OrganizationShiftRules.ClipSegmentsToDay(segments, day.AddDays(1));

        Assert.Collection(nextDay,
            inside =>
            {
                Assert.Equal("2027-01-05T00:00", inside.StartLocal);
                Assert.Equal("2027-01-05T01:00", inside.EndLocal);
                Assert.False(inside.IsOutsideShift);
            },
            outside =>
            {
                Assert.Equal("2027-01-05T01:00", outside.StartLocal);
                Assert.Equal("2027-01-05T02:00", outside.EndLocal);
                Assert.True(outside.IsOutsideShift);
            });
    }
}
