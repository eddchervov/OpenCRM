using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance;

namespace OpenCRM.Modules.Maintenance.Tests.Unit;

public sealed class MaintenanceNotificationContentTests
{
    [Fact]
    public void GetChangeEventPrioritizesCancellationThenReadinessThenScheduleChanges()
    {
        var previous = CreateMaintenance();
        var snapshot = MaintenanceSnapshot.From(previous);

        var cancelled = CreateMaintenance();
        cancelled.Status = MaintenanceStatus.Cancelled;
        cancelled.ReadyForHandoverAt = DateTime.UtcNow;
        Assert.Equal("booking_cancelled", MaintenanceNotificationContent.GetChangeEvent(cancelled, snapshot));

        var ready = CreateMaintenance();
        ready.ReadyForHandoverAt = DateTime.UtcNow;
        ready.PlannedDate = previous.PlannedDate!.Value.AddDays(1);
        Assert.Equal("ready", MaintenanceNotificationContent.GetChangeEvent(ready, snapshot));

        var changed = CreateMaintenance();
        changed.PlannedStartTime = TimeSpan.FromHours(10);
        Assert.Equal("booking_changed", MaintenanceNotificationContent.GetChangeEvent(changed, snapshot));
    }

    [Theory]
    [InlineData(MaintenanceStatus.Planned)]
    [InlineData(MaintenanceStatus.InProgress)]
    public void GetChangeEventDetectsEveryScheduleFieldOnlyForActiveOrders(MaintenanceStatus status)
    {
        var previous = CreateMaintenance();
        var snapshot = MaintenanceSnapshot.From(previous);

        var changed = CreateMaintenance();
        changed.Status = status;
        changed.PlannedDate = previous.PlannedDate!.Value.AddDays(1);
        Assert.Equal("booking_changed", MaintenanceNotificationContent.GetChangeEvent(changed, snapshot));

        changed = CreateMaintenance();
        changed.Status = status;
        changed.PlannedEndDate = previous.PlannedEndDate!.Value.AddDays(1);
        Assert.Equal("booking_changed", MaintenanceNotificationContent.GetChangeEvent(changed, snapshot));

        changed = CreateMaintenance();
        changed.Status = status;
        changed.PlannedStartTime = TimeSpan.FromHours(10);
        Assert.Equal("booking_changed", MaintenanceNotificationContent.GetChangeEvent(changed, snapshot));

        changed = CreateMaintenance();
        changed.Status = status;
        changed.PlannedEndTime = TimeSpan.FromHours(11);
        Assert.Equal("booking_changed", MaintenanceNotificationContent.GetChangeEvent(changed, snapshot));
    }

    [Fact]
    public void GetChangeEventReturnsNullForUnrelatedOrNonActiveChanges()
    {
        var maintenance = CreateMaintenance();
        var snapshot = MaintenanceSnapshot.From(maintenance);

        Assert.Null(MaintenanceNotificationContent.GetChangeEvent(maintenance, snapshot));

        maintenance = CreateMaintenance();
        maintenance.Status = MaintenanceStatus.Completed;
        maintenance.PlannedDate = maintenance.PlannedDate!.Value.AddDays(1);
        Assert.Null(MaintenanceNotificationContent.GetChangeEvent(maintenance, snapshot));

        maintenance = CreateMaintenance();
        maintenance.ReadyForHandoverAt = DateTime.UtcNow;
        var previouslyReady = MaintenanceSnapshot.From(maintenance);
        Assert.Null(MaintenanceNotificationContent.GetChangeEvent(maintenance, previouslyReady));

        maintenance.Status = MaintenanceStatus.Cancelled;
        var previouslyCancelled = MaintenanceSnapshot.From(maintenance);
        Assert.Null(MaintenanceNotificationContent.GetChangeEvent(maintenance, previouslyCancelled));
    }

    [Theory]
    [InlineData("booking_created", "I-Мойка", "А123АА", "I-Мойка: запись на автомобиль А123АА оформлена на 12.10.2026 в 09:30.")]
    [InlineData("booking_changed", "I-Мойка", null, "I-Мойка: время записи на автомобиль изменено на 12.10.2026.")]
    [InlineData("booking_cancelled", "I-Мойка", "А123АА", "I-Мойка: запись на автомобиль А123АА отменена.")]
    [InlineData("ready", "I-Мойка", null, "I-Мойка: автомобиль готов к выдаче.")]
    public void BuildBodyFormatsServiceMessages(string eventCode, string organizationName, string? plate, string expected)
    {
        var maintenance = CreateMaintenance();
        maintenance.PlannedStartTime = eventCode == "booking_created" ? TimeSpan.FromHours(9.5) : null;

        Assert.Equal(expected, MaintenanceNotificationContent.BuildBody(
            maintenance, organizationName, null, plate, eventCode));
    }

    [Theory]
    [InlineData("booking_created", "А123АА", "запись на технику А123АА оформлена на 12.10.2026")]
    [InlineData("booking_changed", null, "время записи на технику изменено на 12.10.2026")]
    [InlineData("booking_cancelled", "А123АА", "запись на технику А123АА отменена")]
    [InlineData("ready", "А123АА", "техника А123АА готова к выдаче")]
    [InlineData("ready", null, "техника готова к выдаче")]
    public void BuildBodyUsesFleetVocabulary(string eventCode, string? plate, string expectedPart)
    {
        var maintenance = CreateMaintenance();

        var body = MaintenanceNotificationContent.BuildBody(
            maintenance,
            "Организация",
            OrganizationTypeCodes.Fleet,
            plate,
            eventCode);

        Assert.Contains(expectedPart, body);
    }

    [Fact]
    public void BuildBodyRejectsUnknownEventCode()
    {
        Assert.Throws<InvalidOperationException>(() => MaintenanceNotificationContent.BuildBody(
            CreateMaintenance(), "Организация", null, null, "unknown"));
    }

    private static MaintenanceEntity CreateMaintenance() => new()
    {
        Status = MaintenanceStatus.Planned,
        Date = new DateTime(2026, 10, 12),
        PlannedDate = new DateTime(2026, 10, 12),
        PlannedEndDate = new DateTime(2026, 10, 12),
        PlannedStartTime = TimeSpan.FromHours(9),
        PlannedEndTime = TimeSpan.FromHours(10)
    };
}
