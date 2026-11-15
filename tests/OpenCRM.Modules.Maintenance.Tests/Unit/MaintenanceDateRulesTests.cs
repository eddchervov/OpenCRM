using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance;

namespace OpenCRM.Modules.Maintenance.Tests.Unit;

public sealed class MaintenanceDateRulesTests
{
    [Theory]
    [InlineData("2026-10-11", 2026, 10, 11)]
    [InlineData("11.10.2026", 2026, 10, 11)]
    public void ParseMaintenanceDateTextAcceptsIsoAndRussianDates(string text, int year, int month, int day)
    {
        Assert.Equal(new DateTime(year, month, day), MaintenanceDateRules.ParseMaintenanceDateText(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2026/10/11")]
    [InlineData("31.02.2026")]
    public void ParseMaintenanceDateTextReturnsNullForBlankOrInvalidDates(string? text)
    {
        Assert.Null(MaintenanceDateRules.ParseMaintenanceDateText(text));
    }

    [Fact]
    public void NormalizeAndStoreDatesDiscardTimeAndUseExpectedKinds()
    {
        var value = new DateTime(2026, 10, 11, 23, 45, 12, DateTimeKind.Local);

        var normalized = MaintenanceDateRules.NormalizeMaintenanceDate(value);
        var stored = MaintenanceDateRules.ToStoredMaintenanceDate(value);

        Assert.Equal(new DateTime(2026, 10, 11), normalized);
        Assert.Equal(DateTimeKind.Unspecified, normalized!.Value.Kind);
        Assert.Equal(new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc), stored);
    }

    [Theory]
    [InlineData(MaintenanceStatus.Planned)]
    [InlineData(MaintenanceStatus.InProgress)]
    [InlineData(MaintenanceStatus.Cancelled)]
    public void ApplyStatusAndDatesStoresPlannedDateAndClearsCompletionHandover(MaintenanceStatus status)
    {
        var planned = new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Local);
        var maintenance = CreateMaintenance();

        MaintenanceDateRules.ApplyStatusAndDates(maintenance, status, planned, null);

        Assert.Equal(status, maintenance.Status);
        Assert.Equal(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc), maintenance.PlannedDate);
        Assert.Null(maintenance.CompletedDate);
        Assert.Null(maintenance.ReadyForHandoverAt);
        Assert.Null(maintenance.HandedOverAt);
        Assert.Equal(maintenance.PlannedDate, maintenance.Date);
    }

    [Fact]
    public void ApplyStatusAndDatesStoresCompletedDateAndOptionalPlannedDate()
    {
        var completed = new DateTime(2026, 10, 13, 17, 0, 0, DateTimeKind.Local);
        var maintenance = CreateMaintenance();

        MaintenanceDateRules.ApplyStatusAndDates(maintenance, MaintenanceStatus.Completed, null, completed);

        Assert.Equal(MaintenanceStatus.Completed, maintenance.Status);
        Assert.Null(maintenance.PlannedDate);
        Assert.Equal(new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc), maintenance.CompletedDate);
        Assert.Equal(maintenance.CompletedDate, maintenance.Date);
    }

    [Fact]
    public void ApplyStatusAndDatesRejectsMissingAndUnsupportedDates()
    {
        var maintenance = CreateMaintenance();

        Assert.Throws<InvalidOperationException>(() => MaintenanceDateRules.ApplyStatusAndDates(
            maintenance, MaintenanceStatus.Planned, null, null));
        Assert.Throws<InvalidOperationException>(() => MaintenanceDateRules.ApplyStatusAndDates(
            maintenance, MaintenanceStatus.Completed, null, null));
        Assert.Throws<InvalidOperationException>(() => MaintenanceDateRules.ApplyStatusAndDates(
            maintenance, (MaintenanceStatus)999, null, null));
    }

    [Fact]
    public void ApplyPlannedTimeClearsOptionalTimesAndKeepsDateOnlyWhenOrderHasPlannedDate()
    {
        var date = new DateTime(2026, 10, 11);
        var plannedMaintenance = CreateMaintenance();
        plannedMaintenance.PlannedDate = date;
        var historicalMaintenance = CreateMaintenance();
        historicalMaintenance.Date = date;

        MaintenanceDateRules.ApplyPlannedTime(plannedMaintenance, null, null, null);
        MaintenanceDateRules.ApplyPlannedTime(historicalMaintenance, null, null, null);

        Assert.Null(plannedMaintenance.PlannedStartTime);
        Assert.Null(plannedMaintenance.PlannedEndTime);
        Assert.Equal(date, plannedMaintenance.PlannedEndDate);
        Assert.Null(historicalMaintenance.PlannedEndDate);
    }

    [Fact]
    public void ApplyPlannedTimeDefaultsDurationAndSupportsOvernightBookings()
    {
        var startDate = new DateTime(2026, 10, 11);
        var defaultDuration = CreateMaintenance();
        defaultDuration.PlannedDate = startDate;
        var overnight = CreateMaintenance();
        overnight.PlannedDate = startDate;

        MaintenanceDateRules.ApplyPlannedTime(defaultDuration, TimeSpan.FromHours(9), null, null);
        MaintenanceDateRules.ApplyPlannedTime(
            overnight, TimeSpan.FromHours(22), TimeSpan.FromHours(2), startDate.AddDays(1));

        Assert.Equal(TimeSpan.FromHours(9), defaultDuration.PlannedStartTime);
        Assert.Equal(TimeSpan.FromHours(10), defaultDuration.PlannedEndTime);
        Assert.Equal(startDate, defaultDuration.PlannedEndDate);
        Assert.Equal(TimeSpan.FromHours(22), overnight.PlannedStartTime);
        Assert.Equal(TimeSpan.FromHours(2), overnight.PlannedEndTime);
        Assert.Equal(startDate.AddDays(1), overnight.PlannedEndDate);
    }

    [Fact]
    public void ApplyPlannedTimeRejectsInvalidDateRangesAndClockValues()
    {
        var date = new DateTime(2026, 10, 11);

        Assert.Throws<InvalidOperationException>(() => MaintenanceDateRules.ApplyPlannedTime(
            CreateMaintenance(date), null, null, date.AddDays(-1)));
        Assert.Throws<InvalidOperationException>(() => MaintenanceDateRules.ApplyPlannedTime(
            CreateMaintenance(date), null, TimeSpan.FromHours(10), date.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => MaintenanceDateRules.ApplyPlannedTime(
            CreateMaintenance(date), TimeSpan.FromHours(9), null, date.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => MaintenanceDateRules.ApplyPlannedTime(
            CreateMaintenance(date), null, TimeSpan.FromHours(10), null));
        Assert.Throws<InvalidOperationException>(() => MaintenanceDateRules.ApplyPlannedTime(
            CreateMaintenance(date), TimeSpan.FromHours(-1), null, null));
        Assert.Throws<InvalidOperationException>(() => MaintenanceDateRules.ApplyPlannedTime(
            CreateMaintenance(date), TimeSpan.FromDays(1), null, null));
        Assert.Throws<InvalidOperationException>(() => MaintenanceDateRules.ApplyPlannedTime(
            CreateMaintenance(date), TimeSpan.FromHours(9), TimeSpan.FromDays(1), null));
        Assert.Throws<InvalidOperationException>(() => MaintenanceDateRules.ApplyPlannedTime(
            CreateMaintenance(date), TimeSpan.FromHours(10), TimeSpan.FromHours(10), null));
    }

    private static MaintenanceEntity CreateMaintenance(DateTime? date = null) => new()
    {
        Date = date ?? new DateTime(2026, 10, 11),
        PlannedDate = date
    };
}
