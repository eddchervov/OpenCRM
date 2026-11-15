using OpenCRM.Domain;
using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Planning.Tests.Unit;

public sealed class SchedulerRulesTests
{
    [Theory]
    [InlineData("  звонок  ", "звонок")]
    [InlineData(" ", null)]
    [InlineData(null, null)]
    public void NormalizeNullableTrimsValueAndConvertsBlankToNull(string? value, string? expected)
    {
        Assert.Equal(expected, SchedulerRules.NormalizeNullable(value));
    }

    [Theory]
    [InlineData(" Задача ", "Задача")]
    [InlineData(null, "Укажите задачу.")]
    [InlineData(" ", "Укажите задачу.")]
    public void NormalizeRequiredTrimsOrRejectsMissingValue(string? value, string expected)
    {
        if (value is null or " ")
        {
            var exception = Assert.Throws<InvalidOperationException>(() => SchedulerRules.NormalizeRequired(value, expected));

            Assert.Equal(expected, exception.Message);
            return;
        }

        Assert.Equal(expected, SchedulerRules.NormalizeRequired(value, "ошибка"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(4, 4)]
    [InlineData(400, 365)]
    public void NormalizeRecurrenceIntervalBoundsValues(int? value, int? expected)
    {
        Assert.Equal(expected, SchedulerRules.NormalizeRecurrenceInterval(value));
    }

    [Fact]
    public void NormalizeDateTimeConvertsLocalValuesToUtcAndPreservesMissingValues()
    {
        var local = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Local);

        Assert.Null(SchedulerRules.NormalizeDateTime(null));
        Assert.Equal(local.ToUniversalTime(), SchedulerRules.NormalizeDateTime(local));
    }

    [Fact]
    public void NormalizeFilterTrimsAndLowercasesValues()
    {
        Assert.Equal(string.Empty, SchedulerRules.NormalizeFilter(null));
        Assert.Equal("call", SchedulerRules.NormalizeFilter(" CALL "));
    }

    [Fact]
    public void NormalizeTaskDatesRepairsInvalidEndAndInfersStartForEndOnlyTask()
    {
        var start = new DateTime(2026, 10, 11, 12, 30, 0, DateTimeKind.Utc);
        var invalidRange = new OrganizationSchedulerTask { StartAt = start, EndAt = start };
        var endOnly = new OrganizationSchedulerTask { EndAt = start.AddDays(1) };

        SchedulerRules.NormalizeTaskDates(invalidRange);
        SchedulerRules.NormalizeTaskDates(endOnly);

        Assert.Equal(start.AddHours(1), invalidRange.EndAt);
        Assert.Equal(start.AddDays(1).Date.AddHours(9), endOnly.StartAt);
    }

    [Fact]
    public void NormalizeTaskRecurrenceClearsNonRecurringFieldsAndRequiresStartForRecurringTask()
    {
        var nonRecurring = new OrganizationSchedulerTask
        {
            RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.None,
            RecurrenceInterval = 3,
            RecurrenceUntil = DateTime.UtcNow,
            RecurrenceGeneratedUntil = DateTime.UtcNow
        };

        SchedulerRules.NormalizeTaskRecurrence(nonRecurring);

        Assert.Null(nonRecurring.RecurrenceInterval);
        Assert.Null(nonRecurring.RecurrenceUntil);
        Assert.Null(nonRecurring.RecurrenceGeneratedUntil);

        var recurringWithoutStart = new OrganizationSchedulerTask
        {
            RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Daily
        };
        var exception = Assert.Throws<InvalidOperationException>(() => SchedulerRules.NormalizeTaskRecurrence(recurringWithoutStart));

        Assert.Equal("Для повторяющейся задачи укажите дату и время начала.", exception.Message);

        var recurring = new OrganizationSchedulerTask
        {
            StartAt = DateTime.UtcNow,
            RecurrenceCode = OrganizationSchedulerTaskRecurrenceCodes.Weekly,
            RecurrenceInterval = 0
        };
        SchedulerRules.NormalizeTaskRecurrence(recurring);
        Assert.Equal(1, recurring.RecurrenceInterval);
    }

    [Theory]
    [InlineData(OrganizationSchedulerTaskRecurrenceCodes.Daily, 2, 2)]
    [InlineData(OrganizationSchedulerTaskRecurrenceCodes.Weekly, 2, 14)]
    [InlineData(OrganizationSchedulerTaskRecurrenceCodes.Monthly, 2, 59)]
    public void NextRecurrenceStartUsesRequestedInterval(string recurrenceCode, int interval, int expectedDays)
    {
        var start = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);

        var next = SchedulerRules.NextRecurrenceStart(start, recurrenceCode, interval);

        Assert.Equal(start.AddDays(expectedDays), next);
    }

    [Fact]
    public void NextRecurrenceStartClampsMonthlyIntervalAndKeepsStartForUnknownCode()
    {
        var start = new DateTime(2026, 1, 31, 9, 0, 0, DateTimeKind.Utc);

        Assert.Equal(start.AddMonths(120), SchedulerRules.NextRecurrenceStart(start, OrganizationSchedulerTaskRecurrenceCodes.Monthly, 365));
        Assert.Equal(start, SchedulerRules.NextRecurrenceStart(start, "unknown", 1));
    }

    [Theory]
    [InlineData(MaintenanceStatus.Completed, OrganizationSchedulerTaskStatusCodes.Closed)]
    [InlineData(MaintenanceStatus.Cancelled, OrganizationSchedulerTaskStatusCodes.Closed)]
    [InlineData(MaintenanceStatus.Planned, OrganizationSchedulerTaskStatusCodes.InProgress)]
    [InlineData(MaintenanceStatus.InProgress, OrganizationSchedulerTaskStatusCodes.InProgress)]
    [InlineData((MaintenanceStatus)0, OrganizationSchedulerTaskStatusCodes.New)]
    public void MaintenanceWorkflowStatusMapsToSchedulerStatus(MaintenanceStatus status, string expected)
    {
        Assert.Equal(expected, SchedulerRules.MapSchedulerTaskStatusCode(status));
    }

    [Theory]
    [InlineData(OrganizationSchedulerTaskStatusCodes.InProgress, "text-bg-warning")]
    [InlineData(OrganizationSchedulerTaskStatusCodes.Deferred, "text-bg-secondary")]
    [InlineData(OrganizationSchedulerTaskStatusCodes.Closed, "text-bg-success")]
    [InlineData(OrganizationSchedulerTaskStatusCodes.New, "text-bg-info")]
    public void TaskStatusMapsToCalendarBadge(string status, string expected)
    {
        Assert.Equal(expected, SchedulerRules.GetTaskStatusBadgeClass(status));
    }

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData(" Машина ", " Позывной ", " Клиент ", "Машина • Позывной • Клиент")]
    [InlineData(" ", null, " Клиент ", "Клиент")]
    public void BuildCalendarSubtitleIncludesOnlyTrimmedValues(string? vehicleTitle, string? vehicleName, string? clientName, string? expected)
    {
        Assert.Equal(expected, SchedulerRules.BuildCalendarSubtitle(vehicleTitle, vehicleName, clientName));
    }

    [Fact]
    public void BuildCalendarCommentCombinesOnlyPresentTaskDetails()
    {
        var comment = SchedulerRules.BuildCalendarComment(
            " Осмотреть автомобиль ",
            OrganizationSchedulerTaskRecurrenceCodes.Weekly,
            new DateTime(2026, 10, 12, 8, 30, 0, DateTimeKind.Utc),
            Guid.NewGuid(),
            null);

        Assert.Contains("Осмотреть автомобиль", comment);
        Assert.Contains("Напомнить 12.10.2026 08:30", comment);
        Assert.Contains("Связана с неисправностью", comment);
        Assert.DoesNotContain("Связана с обслуживанием", comment);

        var fullComment = SchedulerRules.BuildCalendarComment("", OrganizationSchedulerTaskRecurrenceCodes.None,
            null, null, Guid.NewGuid());
        Assert.Equal("Связана с обслуживанием", fullComment);
        Assert.Equal(string.Empty, SchedulerRules.BuildCalendarComment(" ", OrganizationSchedulerTaskRecurrenceCodes.None,
            null, null, null));
    }
}
