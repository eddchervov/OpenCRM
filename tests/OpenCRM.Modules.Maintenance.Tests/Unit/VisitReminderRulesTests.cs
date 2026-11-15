using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance.Reminders;

namespace OpenCRM.Modules.Maintenance.Tests.Unit;

public sealed class VisitReminderRulesTests
{
    [Fact]
    public void UsesOrganizationTimeZoneAcrossUtcMidnight()
    {
        var maintenance = new MaintenanceEntity { PlannedDate = new DateTime(2026, 10, 25), PlannedStartTime = TimeSpan.FromHours(2) };

        Assert.Equal(new DateTime(2026, 10, 24, 19, 0, 0, DateTimeKind.Utc),
            MaintenanceVisitReminderRules.GetStartsAtUtc(maintenance, "Asia/Novosibirsk"));
        Assert.Null(MaintenanceVisitReminderRules.GetStartsAtUtc(maintenance, null));
        Assert.Null(MaintenanceVisitReminderRules.GetStartsAtUtc(maintenance, "invalid-time-zone"));
    }

    [Theory]
    [InlineData(2026, 3, 8, 2)]
    [InlineData(2026, 11, 1, 1)]
    public void DoesNotGuessInvalidOrAmbiguousDstTime(int year, int month, int day, int hour)
    {
        var maintenance = new MaintenanceEntity { PlannedDate = new DateTime(year, month, day), PlannedStartTime = TimeSpan.FromHours(hour).Add(TimeSpan.FromMinutes(30)) };

        Assert.Null(MaintenanceVisitReminderRules.GetStartsAtUtc(maintenance, "America/New_York"));
    }

    [Fact]
    public void MoveCancelDisableAndPastInvalidateQueuedReminder()
    {
        var maintenance = new MaintenanceEntity { PlannedDate = new DateTime(2026, 10, 25), PlannedStartTime = TimeSpan.FromHours(10), Status = MaintenanceStatus.Planned };
        var organization = new Organization { ShiftTimeZoneId = "UTC", VisitReminderHoursBefore = 24 };
        var startsAt = MaintenanceVisitReminderRules.GetStartsAtUtc(maintenance, "UTC");
        var now = startsAt!.Value.AddHours(-1);
        Assert.True(MaintenanceVisitReminderRules.IsCurrent(maintenance, organization, startsAt, now));
        maintenance.PlannedStartTime = TimeSpan.FromHours(11);
        Assert.False(MaintenanceVisitReminderRules.IsCurrent(maintenance, organization, startsAt, now));
        maintenance.PlannedStartTime = TimeSpan.FromHours(10);
        maintenance.Status = MaintenanceStatus.Cancelled;
        Assert.False(MaintenanceVisitReminderRules.IsCurrent(maintenance, organization, startsAt, now));
        maintenance.Status = MaintenanceStatus.Planned;
        maintenance.VisitReminderEnabled = false;
        Assert.False(MaintenanceVisitReminderRules.IsCurrent(maintenance, organization, startsAt, now));
        maintenance.VisitReminderEnabled = true;
        Assert.False(MaintenanceVisitReminderRules.IsCurrent(maintenance, organization, startsAt, startsAt.Value));
    }

    [Theory]
    [InlineData("{unknown}")]
    [InlineData("{date")]
    [InlineData("date}")]
    [InlineData("")]
    public void RejectsUnknownOrMalformedTemplate(string value)
    {
        Assert.Throws<ArgumentException>(() => VisitReminderContent.Validate(value));
    }

    [Fact]
    public void ValuesAreNotRecursivelyInterpretedAsTemplateFields()
    {
        var maintenance = new MaintenanceEntity { Vehicle = new Vehicle { RegistrationPlate = "А123АА154" }, PlannedDate = new DateTime(2026, 10, 25), PlannedStartTime = TimeSpan.FromHours(10) };
        var organization = new Organization { Name = "{client}", VisitReminderTemplate = "{organization}: {client}, {date} {time}, {vehicle}" };

        Assert.Equal("{client}: Анна, 25.10.2026 10:00, А123АА154", VisitReminderContent.Build(maintenance, organization, "Анна"));
    }

    [Fact]
    public void ChangingLeadTimePostponesOldQueueAndUsesDifferentIdempotencyKey()
    {
        var maintenance = new MaintenanceEntity { PlannedDate = new DateTime(2026, 10, 25), PlannedStartTime = TimeSpan.FromHours(10), Status = MaintenanceStatus.Planned };
        var organization = new Organization { ShiftTimeZoneId = "UTC", VisitReminderHoursBefore = 24 };
        var startsAt = MaintenanceVisitReminderRules.GetStartsAtUtc(maintenance, "UTC")!.Value;
        var oldKey = MaintenanceVisitReminderRules.GetNotificationKey(maintenance.Id, startsAt, 24, "telegram");
        organization.VisitReminderHoursBefore = 2;

        Assert.False(MaintenanceVisitReminderRules.IsCurrent(maintenance, organization, startsAt, startsAt.AddHours(-20)));
        Assert.True(MaintenanceVisitReminderRules.IsCurrent(maintenance, organization, startsAt, startsAt.AddHours(-2)));
        Assert.NotEqual(oldKey, MaintenanceVisitReminderRules.GetNotificationKey(maintenance.Id, startsAt, 2, "telegram"));
    }

    [Fact]
    public void TemplateCannotOverflowChannelLimitAfterExpansion()
    {
        Assert.Throws<ArgumentException>(() => VisitReminderContent.Validate(string.Concat(Enumerable.Repeat("{organization}", 30))));
        VisitReminderContent.Validate(MaintenanceVisitReminderRules.DefaultTemplate);
    }
}
