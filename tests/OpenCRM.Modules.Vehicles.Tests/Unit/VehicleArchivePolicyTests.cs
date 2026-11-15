using OpenCRM.Modules.Vehicles;

namespace OpenCRM.Modules.Vehicles.Tests.Unit;

public sealed class VehicleArchivePolicyTests
{
    private static readonly DateTime s_now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(-29, -23, -59, 0, false)]
    [InlineData(-30, 0, 0, 0, true)]
    [InlineData(-30, 0, 0, -1, true)]
    [InlineData(-40, 0, 0, 0, true)]
    [InlineData(0, 12, 0, 0, false)]
    [InlineData(1, 12, 0, 0, false)]
    public void IsExpiredUsesThirtyDayBoundary(
        int daysFromNow,
        int hoursFromNow,
        int minutesFromNow,
        int secondsFromNow,
        bool expected)
    {
        var archiveDate = VehicleArchivePolicyTests.s_now.AddDays(daysFromNow).AddHours(hoursFromNow).AddMinutes(minutesFromNow).AddSeconds(secondsFromNow);

        Assert.Equal(expected, VehicleArchivePolicy.IsExpired(archiveDate, VehicleArchivePolicyTests.s_now));
    }

    [Theory]
    [InlineData(-29, -23, -59, 0, 1)]
    [InlineData(-30, 0, 0, 0, 0)]
    [InlineData(-30, 0, 0, -1, 0)]
    [InlineData(-40, 0, 0, 0, 0)]
    [InlineData(0, 12, 0, 0, 30)]
    [InlineData(1, 12, 0, 0, 31)]
    public void GetDaysLeftPreservesExistingDateRounding(
        int daysFromNow,
        int hoursFromNow,
        int minutesFromNow,
        int secondsFromNow,
        int expected)
    {
        var archiveDate = VehicleArchivePolicyTests.s_now.AddDays(daysFromNow).AddHours(hoursFromNow).AddMinutes(minutesFromNow).AddSeconds(secondsFromNow);

        Assert.Equal(expected, VehicleArchivePolicy.GetDaysLeft(archiveDate, VehicleArchivePolicyTests.s_now));
    }

    [Fact]
    public void GetDaysLeftReturnsNullWhenArchiveDateIsMissing()
    {
        Assert.Null(VehicleArchivePolicy.GetDaysLeft(null, VehicleArchivePolicyTests.s_now));
    }
}
