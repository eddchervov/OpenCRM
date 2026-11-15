using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using Moq;

namespace OpenCRM.Modules.Maintenance.Tests.Unit;

public sealed class MaintenanceWorkflowRulesTests
{
    [Theory]
    [InlineData(" START ", "start")]
    [InlineData("Complete", "complete")]
    [InlineData("CANCEL", "cancel")]
    [InlineData("reschedule", "reschedule")]
    [InlineData("reopen", "reopen")]
    [InlineData("return_to_planned", "return_to_planned")]
    [InlineData("return_to_work", "return_to_work")]
    [InlineData("mark_ready", "mark_ready")]
    [InlineData("handover", "handover")]
    [InlineData("simple_handover", "simple_handover")]
    [InlineData("undo_handover", "undo_handover")]
    public void NormalizeWorkflowActionTrimsAndNormalizesSupportedActions(string action, string expected)
    {
        Assert.Equal(expected, MaintenanceWorkflowRules.NormalizeWorkflowAction(action));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("finish")]
    public void NormalizeWorkflowActionRejectsUnknownActions(string? action)
    {
        Assert.Throws<InvalidOperationException>(() => MaintenanceWorkflowRules.NormalizeWorkflowAction(action));
    }

    [Theory]
    [InlineData("09:15", 9, 15, 0)]
    [InlineData("09:15:30", 9, 15, 30)]
    public void ParsePlannedTimeTextAcceptsSupportedFormats(string text, int hour, int minute, int second)
    {
        Assert.Equal(new TimeSpan(hour, minute, second), MaintenanceWorkflowRules.ParsePlannedTimeText(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9:15")]
    [InlineData("25:00")]
    [InlineData("09:15:30.123")]
    public void ParsePlannedTimeTextReturnsNullForUnsupportedFormats(string? text)
    {
        Assert.Null(MaintenanceWorkflowRules.ParsePlannedTimeText(text));
    }

    [Fact]
    public void ApplyWorkflowPlannedTimeUsesTextThenValuesThenExistingTimesAndDefaults()
    {
        var date = new DateTime(2026, 10, 11);
        var maintenance = CreateMaintenance(MaintenanceStatus.Planned, date);
        maintenance.PlannedStartTime = TimeSpan.FromHours(7);
        maintenance.PlannedEndTime = TimeSpan.FromHours(8);
        var defaults = CreateMaintenance(MaintenanceStatus.Planned, date);

        MaintenanceWorkflowRules.ApplyWorkflowPlannedTime(maintenance, new MaintenanceWorkflowActionDto
        {
            Action = "reschedule",
            PlannedStartTimeText = "10:30",
            PlannedEndTimeText = "12:00"
        });
        MaintenanceWorkflowRules.ApplyWorkflowPlannedTime(defaults, new MaintenanceWorkflowActionDto
        {
            Action = "reschedule"
        });

        Assert.Equal(TimeSpan.FromHours(10.5), maintenance.PlannedStartTime);
        Assert.Equal(TimeSpan.FromHours(12), maintenance.PlannedEndTime);
        Assert.Equal(TimeSpan.FromHours(9), defaults.PlannedStartTime);
        Assert.Equal(TimeSpan.FromHours(10), defaults.PlannedEndTime);
    }

    [Fact]
    public void ApplyWorkflowActionStartsCompletesAndCancelsValidOrders()
    {
        var now = new DateTime(2026, 10, 11, 16, 0, 0, DateTimeKind.Utc);
        var rules = CreateRules(now);
        var planned = CreateMaintenance(MaintenanceStatus.Planned, now.Date);
        var cancelled = CreateMaintenance(MaintenanceStatus.Planned, now.Date);
        var inProgress = CreateMaintenance(MaintenanceStatus.InProgress, now.Date);

        Assert.Equal("Обслуживание взято в работу", rules.ApplyWorkflowAction(
            planned, "start", new MaintenanceWorkflowActionDto { Action = "start" }, false));
        Assert.Equal(MaintenanceStatus.InProgress, planned.Status);

        Assert.Equal("Обслуживание отменено", rules.ApplyWorkflowAction(
            cancelled, "cancel", new MaintenanceWorkflowActionDto { Action = "cancel" }, false));
        Assert.Equal(MaintenanceStatus.Cancelled, cancelled.Status);

        var message = rules.ApplyWorkflowAction(inProgress, "complete", new MaintenanceWorkflowActionDto
        {
            Action = "complete",
            CompletedDateText = "10.10.2026",
            CompletedDate = now.AddDays(-5),
            Mileage = 25000
        }, true);

        Assert.Equal("Работы завершены: техника готова к выдаче", message);
        Assert.Equal(MaintenanceStatus.Completed, inProgress.Status);
        Assert.Equal(new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), inProgress.CompletedDate);
        Assert.Equal(25000, inProgress.Mileage);
        Assert.Equal(now, inProgress.ReadyForHandoverAt);
    }

    [Fact]
    public void OrganizationCompletionUsesLocalDateWhilePersonalCompletionKeepsUtcDate()
    {
        var now = new DateTime(2026, 10, 24, 20, 30, 0, DateTimeKind.Utc);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Novosibirsk");
        var rules = CreateRules(now);
        var organizationMaintenance = CreateMaintenance(MaintenanceStatus.InProgress, now.Date);
        var personalMaintenance = CreateMaintenance(MaintenanceStatus.InProgress, now.Date);

        rules.ApplyWorkflowAction(
            organizationMaintenance,
            "complete",
            new MaintenanceWorkflowActionDto { Action = "complete" },
            true,
            organizationTimeZone: timeZone);
        rules.ApplyWorkflowAction(
            personalMaintenance,
            "complete",
            new MaintenanceWorkflowActionDto { Action = "complete" },
            false);

        Assert.Equal(new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc), organizationMaintenance.CompletedDate);
        Assert.Equal(new DateTime(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc), personalMaintenance.CompletedDate);
    }

    [Fact]
    public void OrganizationSimpleHandoverUsesLocalCompletionDate()
    {
        var now = new DateTime(2026, 10, 24, 20, 30, 0, DateTimeKind.Utc);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Novosibirsk");
        var rules = CreateRules(now);
        var maintenance = CreateMaintenance(MaintenanceStatus.InProgress, now.Date);

        rules.ApplyWorkflowAction(
            maintenance,
            "simple_handover",
            new MaintenanceWorkflowActionDto { Action = "simple_handover" },
            true,
            isSimpleWorkflow: true,
            organizationTimeZone: timeZone);

        Assert.Equal(new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc), maintenance.CompletedDate);
    }

    [Fact]
    public void ApplyWorkflowActionSupportsReturnAndRescheduleTransitions()
    {
        var now = new DateTime(2026, 10, 11, 16, 0, 0, DateTimeKind.Utc);
        var rules = CreateRules(now);
        var inProgress = CreateMaintenance(MaintenanceStatus.InProgress, now.Date);
        var completed = CreateMaintenance(MaintenanceStatus.Completed, now.Date);
        var rescheduled = CreateMaintenance(MaintenanceStatus.Planned, now.Date);
        var cancelled = CreateMaintenance(MaintenanceStatus.Cancelled, now.Date);

        Assert.Equal("Запись возвращена в запланированные", rules.ApplyWorkflowAction(
            inProgress, "return_to_planned", new MaintenanceWorkflowActionDto { Action = "return_to_planned" }, true));
        Assert.Equal(MaintenanceStatus.Planned, inProgress.Status);

        Assert.Equal("Выполнение отменено: запись возвращена в работу", rules.ApplyWorkflowAction(
            completed, "return_to_work", new MaintenanceWorkflowActionDto { Action = "return_to_work" }, true));
        Assert.Equal(MaintenanceStatus.InProgress, completed.Status);
        Assert.Null(completed.CompletedDate);

        Assert.Equal("Обслуживание перенесено", rules.ApplyWorkflowAction(rescheduled, "reschedule",
            new MaintenanceWorkflowActionDto
            {
                Action = "reschedule",
                PlannedDate = now.AddDays(2),
                PlannedDateText = "14.10.2026",
                PlannedStartTime = TimeSpan.FromHours(10),
                PlannedEndTime = TimeSpan.FromHours(12)
            }, true));
        Assert.Equal(new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc), rescheduled.PlannedDate);
        Assert.Equal(TimeSpan.FromHours(10), rescheduled.PlannedStartTime);

        Assert.Equal("Обслуживание возобновлено", rules.ApplyWorkflowAction(cancelled, "reopen",
            new MaintenanceWorkflowActionDto { Action = "reopen", PlannedDate = now.AddDays(3) }, true));
        Assert.Equal(MaintenanceStatus.Planned, cancelled.Status);
        Assert.Equal(new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc), cancelled.PlannedDate);
    }

    [Fact]
    public void ApplyWorkflowActionFallsBackToOrderDateWhenPlannedDateIsMissing()
    {
        var date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        var rules = CreateRules(date);
        var started = new MaintenanceEntity { Status = MaintenanceStatus.Planned, Date = date };
        var cancelled = new MaintenanceEntity { Status = MaintenanceStatus.Planned, Date = date };
        var returnedToPlan = new MaintenanceEntity { Status = MaintenanceStatus.InProgress, Date = date };
        var returnedToWork = new MaintenanceEntity { Status = MaintenanceStatus.Completed, Date = date };

        rules.ApplyWorkflowAction(started, "start", new MaintenanceWorkflowActionDto { Action = "start" }, true);
        rules.ApplyWorkflowAction(cancelled, "cancel", new MaintenanceWorkflowActionDto { Action = "cancel" }, true);
        rules.ApplyWorkflowAction(returnedToPlan, "return_to_planned",
            new MaintenanceWorkflowActionDto { Action = "return_to_planned" }, true);
        rules.ApplyWorkflowAction(returnedToWork, "return_to_work",
            new MaintenanceWorkflowActionDto { Action = "return_to_work" }, true);

        Assert.All([started, cancelled, returnedToPlan, returnedToWork], maintenance =>
            Assert.Equal(date, maintenance.PlannedDate));
    }

    [Fact]
    public void ReopenUsesParsedPlannedDateTextWhenBothDateInputsAreAvailable()
    {
        var now = new DateTime(2026, 10, 11, 16, 0, 0, DateTimeKind.Utc);
        var maintenance = CreateMaintenance(MaintenanceStatus.Cancelled, now.Date);

        var message = CreateRules(now).ApplyWorkflowAction(maintenance, "reopen", new MaintenanceWorkflowActionDto
        {
            Action = "reopen",
            PlannedDate = now.AddDays(1),
            PlannedDateText = "14.10.2026"
        }, true);

        Assert.Equal("Обслуживание возобновлено", message);
        Assert.Equal(new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc), maintenance.PlannedDate);
    }

    [Fact]
    public void ApplyWorkflowPlannedTimeFallsBackFromInvalidTextToDtoAndExistingTimes()
    {
        var maintenance = CreateMaintenance(MaintenanceStatus.Planned, new DateTime(2026, 10, 11));
        maintenance.PlannedStartTime = TimeSpan.FromHours(7);
        maintenance.PlannedEndTime = TimeSpan.FromHours(11);

        MaintenanceWorkflowRules.ApplyWorkflowPlannedTime(maintenance, new MaintenanceWorkflowActionDto
        {
            Action = "reschedule",
            PlannedStartTimeText = "7:30",
            PlannedEndTimeText = "invalid",
            PlannedStartTime = TimeSpan.FromHours(9)
        });

        Assert.Equal(TimeSpan.FromHours(9), maintenance.PlannedStartTime);
        Assert.Equal(TimeSpan.FromHours(11), maintenance.PlannedEndTime);
    }

    [Fact]
    public void ApplyWorkflowActionMarksReadyHandsOverAndCanUndoHandover()
    {
        var now = new DateTime(2026, 10, 11, 16, 0, 0, DateTimeKind.Utc);
        var rules = CreateRules(now);
        var completed = CreateMaintenance(MaintenanceStatus.Completed, now.Date);

        Assert.Equal("Техника отмечена готовой к выдаче", rules.ApplyWorkflowAction(
            completed, "mark_ready", new MaintenanceWorkflowActionDto { Action = "mark_ready" }, true));
        Assert.Equal(now, completed.ReadyForHandoverAt);

        Assert.Equal("Техника выдана клиенту", rules.ApplyWorkflowAction(
            completed, "handover", new MaintenanceWorkflowActionDto { Action = "handover" }, true));
        Assert.Equal(now, completed.HandedOverAt);

        Assert.Equal("Выдача отменена: техника снова ожидает клиента", rules.ApplyWorkflowAction(
            completed, "undo_handover", new MaintenanceWorkflowActionDto
            {
                Action = "undo_handover",
                Reason = "Покупатель вернулся"
            }, true));
        Assert.Null(completed.HandedOverAt);
    }

    [Theory]
    [InlineData(MaintenanceStatus.Planned)]
    [InlineData(MaintenanceStatus.InProgress)]
    public void ApplyWorkflowActionCompletesAndHandsOverPlannedOrInProgressWork(MaintenanceStatus status)
    {
        var now = new DateTime(2026, 10, 23, 12, 30, 45, DateTimeKind.Utc);
        var maintenance = CreateMaintenance(status, now.Date.AddDays(1));

        var message = CreateRules(now).ApplyWorkflowAction(maintenance, "simple_handover",
            new MaintenanceWorkflowActionDto { Action = "simple_handover" }, true, true);

        Assert.Equal("Работы завершены и техника выдана клиенту", message);
        Assert.Equal(MaintenanceStatus.Completed, maintenance.Status);
        Assert.Equal(now.Date, maintenance.CompletedDate);
        Assert.Equal(now.Date.AddDays(1), maintenance.PlannedDate);
        Assert.Equal(now, maintenance.ReadyForHandoverAt);
        Assert.Equal(now, maintenance.HandedOverAt);
    }

    [Fact]
    public void ApplyWorkflowActionPreservesCompletionFactsForLegacyCompletedWork()
    {
        var now = new DateTime(2026, 10, 23, 12, 30, 45, DateTimeKind.Utc);
        var completedAt = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        var readyAt = new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);
        var maintenance = CreateMaintenance(MaintenanceStatus.Completed, completedAt);
        maintenance.Date = completedAt;
        maintenance.CompletedDate = completedAt;
        maintenance.ReadyForHandoverAt = readyAt;
        maintenance.Mileage = 35000;

        CreateRules(now).ApplyWorkflowAction(maintenance, "simple_handover",
            new MaintenanceWorkflowActionDto { Action = "simple_handover" }, true, true);

        Assert.Equal(MaintenanceStatus.Completed, maintenance.Status);
        Assert.Equal(completedAt, maintenance.Date);
        Assert.Equal(completedAt, maintenance.CompletedDate);
        Assert.Equal(readyAt, maintenance.ReadyForHandoverAt);
        Assert.Equal(now, maintenance.HandedOverAt);
        Assert.Equal(35000, maintenance.Mileage);
    }

    [Fact]
    public void ApplyWorkflowActionRejectsInvalidStatusesWorkspaceAndMissingReasons()
    {
        var rules = CreateRules(DateTime.UtcNow);
        var completed = CreateMaintenance(MaintenanceStatus.Completed, DateTime.UtcNow.Date);
        var planned = CreateMaintenance(MaintenanceStatus.Planned, DateTime.UtcNow.Date);
        var inProgress = CreateMaintenance(MaintenanceStatus.InProgress, DateTime.UtcNow.Date);
        var handedOver = CreateMaintenance(MaintenanceStatus.Completed, DateTime.UtcNow.Date);
        handedOver.HandedOverAt = DateTime.UtcNow;

        Assert.Throws<InvalidOperationException>(() => rules.ApplyWorkflowAction(
            completed, "start", new MaintenanceWorkflowActionDto { Action = "start" }, true));
        Assert.Throws<InvalidOperationException>(() => rules.ApplyWorkflowAction(
            planned, "mark_ready", new MaintenanceWorkflowActionDto { Action = "mark_ready" }, false));
        Assert.Throws<InvalidOperationException>(() => rules.ApplyWorkflowAction(
            planned, "simple_handover", new MaintenanceWorkflowActionDto { Action = "simple_handover" }, true));
        Assert.Throws<InvalidOperationException>(() => rules.ApplyWorkflowAction(
            inProgress, "handover", new MaintenanceWorkflowActionDto { Action = "handover" }, false));
        Assert.Throws<InvalidOperationException>(() => rules.ApplyWorkflowAction(
            handedOver, "undo_handover", new MaintenanceWorkflowActionDto { Action = "undo_handover" }, true));
        Assert.Throws<InvalidOperationException>(() => rules.ApplyWorkflowAction(
            handedOver, "return_to_work", new MaintenanceWorkflowActionDto { Action = "return_to_work" }, true));
        Assert.Throws<InvalidOperationException>(() => rules.ApplyWorkflowAction(
            completed, "cancel", new MaintenanceWorkflowActionDto { Action = "cancel" }, true));
        Assert.Throws<InvalidOperationException>(() => rules.ApplyWorkflowAction(
            completed, "reschedule", new MaintenanceWorkflowActionDto { Action = "reschedule" }, true));
        Assert.Throws<InvalidOperationException>(() => rules.ApplyWorkflowAction(
            planned, "reopen", new MaintenanceWorkflowActionDto { Action = "reopen" }, true));
        Assert.Throws<InvalidOperationException>(() => rules.ApplyWorkflowAction(
            planned, "unsupported", new MaintenanceWorkflowActionDto { Action = "unsupported" }, true));
    }

    [Theory]
    [InlineData(MaintenanceStatus.Planned, MaintenanceStatus.Planned)]
    [InlineData(MaintenanceStatus.Planned, MaintenanceStatus.InProgress)]
    [InlineData(MaintenanceStatus.Planned, MaintenanceStatus.Cancelled)]
    [InlineData(MaintenanceStatus.InProgress, MaintenanceStatus.Cancelled)]
    [InlineData(MaintenanceStatus.Cancelled, MaintenanceStatus.Planned)]
    public void ValidateWorkspaceStatusChangeAcceptsSupportedTransitions(
        MaintenanceStatus previous, MaintenanceStatus next)
    {
        MaintenanceWorkflowRules.ValidateWorkspaceStatusChange(previous, next);
    }

    [Theory]
    [InlineData(MaintenanceStatus.Planned, MaintenanceStatus.Completed)]
    [InlineData(MaintenanceStatus.InProgress, MaintenanceStatus.Planned)]
    [InlineData(MaintenanceStatus.Completed, MaintenanceStatus.Planned)]
    [InlineData(MaintenanceStatus.Cancelled, MaintenanceStatus.InProgress)]
    public void ValidateWorkspaceStatusChangeRejectsUnsupportedTransitions(
        MaintenanceStatus previous, MaintenanceStatus next)
    {
        Assert.Throws<InvalidOperationException>(() =>
            MaintenanceWorkflowRules.ValidateWorkspaceStatusChange(previous, next));
    }

    [Fact]
    public void BuildWorkflowHistoryDescriptionIncludesChangedFieldsAndOptionalReason()
    {
        var maintenance = CreateMaintenance(MaintenanceStatus.Planned, new DateTime(2026, 10, 11));
        var previous = MaintenanceSnapshot.From(maintenance);

        Assert.Equal(string.Empty, MaintenanceWorkflowRules.BuildWorkflowHistoryDescription(
            previous, maintenance, "   "));

        maintenance.Location = "Пост 2";
        maintenance.Mileage = 25000;
        var description = MaintenanceWorkflowRules.BuildWorkflowHistoryDescription(
            previous, maintenance, "  Осмотр завершен  ");

        Assert.Contains("Пост / место: не указано -> Пост 2", description);
        Assert.Contains("Пробег/наработка: не указано -> 25000", description);
        Assert.Contains("Комментарий: Осмотр завершен", description);
    }

    private static MaintenanceWorkflowRules CreateRules(DateTime now)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(now);
        clock.SetupGet(value => value.UtcToday).Returns(now.Date);

        return new MaintenanceWorkflowRules(clock.Object);
    }

    private static MaintenanceEntity CreateMaintenance(MaintenanceStatus status, DateTime date) => new()
    {
        Status = status,
        Date = date,
        PlannedDate = date
    };
}
