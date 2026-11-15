using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Tests.Unit;

public sealed class MaintenanceCommandRulesEdgeTests
{
    [Theory]
    [InlineData("complete", MaintenanceStatus.Completed)]
    [InlineData("complete", MaintenanceStatus.Cancelled)]
    [InlineData("undo_handover", MaintenanceStatus.Completed)]
    [InlineData("reschedule", MaintenanceStatus.Planned)]
    [InlineData("reschedule", MaintenanceStatus.InProgress)]
    [InlineData("reopen", MaintenanceStatus.Cancelled)]
    public void InvalidWorkflowRequestDoesNotChangeOrder(string action, MaintenanceStatus status)
    {
        var date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        var work = new MaintenanceEntity
        {
            Status = status,
            Date = date,
            PlannedDate = date,
            HandedOverAt = action == "undo_handover" ? date : null
        };
        var rules = new MaintenanceWorkflowRules(Mock.Of<ISystemClock>());
        var request = new MaintenanceWorkflowActionDto { Action = action, Reason = " " };

        Assert.Throws<InvalidOperationException>(() => rules.ApplyWorkflowAction(work, action, request, true));
        Assert.Equal(status, work.Status);
        Assert.Equal(date, work.Date);
        Assert.Equal(date, work.PlannedDate);
        Assert.Equal(action == "undo_handover" ? date : (DateTime?)null, work.HandedOverAt);
    }

    [Theory]
    [InlineData("start", MaintenanceStatus.InProgress, false, false)]
    [InlineData("start", MaintenanceStatus.Completed, false, false)]
    [InlineData("mark_ready", MaintenanceStatus.Planned, false, false)]
    [InlineData("mark_ready", MaintenanceStatus.Completed, true, false)]
    [InlineData("handover", MaintenanceStatus.Planned, false, false)]
    [InlineData("handover", MaintenanceStatus.Completed, false, false)]
    [InlineData("handover", MaintenanceStatus.Completed, true, true)]
    [InlineData("undo_handover", MaintenanceStatus.Planned, false, true)]
    [InlineData("undo_handover", MaintenanceStatus.Completed, true, false)]
    [InlineData("return_to_planned", MaintenanceStatus.Planned, false, false)]
    [InlineData("return_to_work", MaintenanceStatus.Planned, false, false)]
    [InlineData("cancel", MaintenanceStatus.Cancelled, false, false)]
    [InlineData("reschedule", MaintenanceStatus.Cancelled, false, false)]
    public void WorkflowRejectsUnavailableTransitionBeforeChangingOrder(
        string action, MaintenanceStatus status, bool hasReadyDate, bool hasHandoverDate)
    {
        var date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        var work = new MaintenanceEntity
        {
            Status = status,
            Date = date,
            PlannedDate = date,
            ReadyForHandoverAt = hasReadyDate ? date : null,
            HandedOverAt = hasHandoverDate ? date : null
        };
        var previous = MaintenanceSnapshot.From(work);
        var rules = new MaintenanceWorkflowRules(Mock.Of<ISystemClock>());

        Assert.Throws<InvalidOperationException>(() => rules.ApplyWorkflowAction(work, action,
            new MaintenanceWorkflowActionDto { Action = action, Reason = "Причина" }, true));
        Assert.False(MaintenanceChangeRules.HasMaintenanceChanges(previous, work));
    }

    [Fact]
    public void PersonalCompletionUsesExplicitDateOrClockAndKeepsMileageWhenOmitted()
    {
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(now);
        var rules = new MaintenanceWorkflowRules(clock.Object);
        var explicitDate = new MaintenanceEntity { Status = MaintenanceStatus.Planned, PlannedDate = now.Date, Mileage = 123 };
        var defaultDate = new MaintenanceEntity { Status = MaintenanceStatus.InProgress, PlannedDate = now.Date };

        rules.ApplyWorkflowAction(explicitDate, "complete", new MaintenanceWorkflowActionDto
        {
            Action = "complete", CompletedDate = now.AddDays(-1), Mileage = null
        }, false);
        rules.ApplyWorkflowAction(defaultDate, "complete", new MaintenanceWorkflowActionDto { Action = "complete" }, false);

        Assert.Equal(now.Date.AddDays(-1), explicitDate.CompletedDate);
        Assert.Equal(123, explicitDate.Mileage);
        Assert.Null(explicitDate.ReadyForHandoverAt);
        Assert.Equal(now.Date, defaultDate.CompletedDate);
        Assert.Null(defaultDate.ReadyForHandoverAt);
    }

    [Fact]
    public void PlannedTimesKeepExistingWindowAndAcceptDtoValues()
    {
        var date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        var work = new MaintenanceEntity
        {
            Status = MaintenanceStatus.Planned, Date = date, PlannedDate = date,
            PlannedStartTime = TimeSpan.FromHours(7), PlannedEndTime = TimeSpan.FromHours(8)
        };
        MaintenanceWorkflowRules.ApplyWorkflowPlannedTime(work, new MaintenanceWorkflowActionDto { Action = "reschedule" });
        Assert.Equal(TimeSpan.FromHours(7), work.PlannedStartTime);
        Assert.Equal(TimeSpan.FromHours(8), work.PlannedEndTime);
        MaintenanceWorkflowRules.ApplyWorkflowPlannedTime(work, new MaintenanceWorkflowActionDto
        {
            Action = "reschedule", PlannedStartTime = TimeSpan.FromHours(10), PlannedEndTime = TimeSpan.FromHours(11)
        });
        Assert.Equal(TimeSpan.FromHours(10), work.PlannedStartTime);
        Assert.Equal(TimeSpan.FromHours(11), work.PlannedEndTime);
    }

    [Fact]
    public void WorkflowHistoryRecordsRemovalOfReadyAndHandoverDates()
    {
        var date = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        var work = new MaintenanceEntity { ReadyForHandoverAt = date, HandedOverAt = date.AddHours(1) };
        var previous = MaintenanceSnapshot.From(work);
        work.ReadyForHandoverAt = null;
        work.HandedOverAt = null;

        var history = MaintenanceWorkflowRules.BuildWorkflowHistoryDescription(previous, work, null);

        Assert.Contains("Готовность к выдаче (UTC)", history);
        Assert.Contains("Выдача (UTC)", history);
        Assert.Contains(date.ToString("O"), history);
        Assert.Contains(date.AddHours(1).ToString("O"), history);
    }

    [Theory]
    [InlineData("EmployeeId")]
    [InlineData("CatalogItemId")]
    [InlineData("MaterialCatalogItemId")]
    public void DetailComparisonDistinguishesAbsentAndAssignedReferences(string field)
    {
        var detail = new MaintenanceDetail { Name = "Произвольная работа" };
        var work = new MaintenanceEntity { Details = [detail] };
        var unlinked = MaintenanceSnapshot.From(work).Details.Single();
        Assert.True(MaintenanceChangeRules.AreSameDetail(unlinked, detail));
        switch (field)
        {
            case "EmployeeId":
                detail.EmployeeId = Guid.NewGuid();
                break;
            case "CatalogItemId":
                detail.CatalogItemId = Guid.NewGuid();
                break;
            case "MaterialCatalogItemId":
                detail.MaterialCatalogItemId = Guid.NewGuid();
                break;
        }

        Assert.False(MaintenanceChangeRules.AreSameDetail(unlinked, detail));
        var linked = MaintenanceSnapshot.From(work).Details.Single();
        detail.EmployeeId = null;
        detail.CatalogItemId = null;
        detail.MaterialCatalogItemId = null;
        Assert.False(MaintenanceChangeRules.AreSameDetail(linked, detail));
        Assert.DoesNotContain("сотрудник:", MaintenanceChangeRules.FormatDetail(unlinked));
    }

    [Theory]
    [InlineData("date")]
    [InlineData("start")]
    [InlineData("end_date")]
    [InlineData("end")]
    public void NotificationDetectsAddingAndRemovingScheduleValues(string field)
    {
        var date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        var empty = new MaintenanceEntity { Status = MaintenanceStatus.Planned, Date = date };
        var scheduled = new MaintenanceEntity { Status = MaintenanceStatus.Planned, Date = date };
        switch (field)
        {
            case "date":
                scheduled.PlannedDate = date;
                break;
            case "start":
                scheduled.PlannedStartTime = TimeSpan.FromHours(9);
                break;
            case "end_date":
                scheduled.PlannedEndDate = date.AddDays(1);
                break;
            case "end":
                scheduled.PlannedEndTime = TimeSpan.FromHours(10);
                break;
        }

        Assert.Equal("booking_changed", MaintenanceNotificationContent.GetChangeEvent(scheduled, MaintenanceSnapshot.From(empty)));
        Assert.Equal("booking_changed", MaintenanceNotificationContent.GetChangeEvent(empty, MaintenanceSnapshot.From(scheduled)));
        Assert.Contains("11.10.2026", MaintenanceNotificationContent.BuildBody(empty, "Студия", null, null, "booking_created"));
    }

    [Fact]
    public void PlannedTimeRejectsNegativeEndAndDetailIdsIgnoreUnkeyedHistoricalRows()
    {
        var date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        Assert.Throws<InvalidOperationException>(() => MaintenanceDateRules.ApplyPlannedTime(
            new MaintenanceEntity { Date = date, PlannedDate = date }, TimeSpan.FromHours(9), TimeSpan.FromMinutes(-1), null));
        var detail = new MaintenanceDetail { DetailId = Guid.Empty, Name = "Работа" };
        MaintenanceDetailRules.AssignDetailIds([detail], [new MaintenanceDetail { DetailId = Guid.Empty, Name = "Работа" }]);
        Assert.NotEqual(Guid.Empty, detail.DetailId);
    }

    [Fact]
    public void SummaryFallsBackToOrderDateAndReportsExplicitEndDate()
    {
        var date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        var work = new MaintenanceEntity
        {
            Status = MaintenanceStatus.Completed, Date = date, PlannedStartTime = TimeSpan.FromHours(9),
            PlannedEndDate = date.AddDays(1)
        };

        var summary = MaintenanceChangeRules.BuildMaintenanceSummary(work);

        Assert.Contains("Дата выполнения: 11.10.2026", summary);
        Assert.Contains("Плановая дата окончания: 12.10.2026", summary);
    }

    [Fact]
    public async Task OrganizationOrderRequiresSeparateWorkAndMaterialLines()
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused;Password=unused")
            .Options;
        await using var context = new OpenCRMContext(options);
        var rules = new MaintenanceDetailRules(context, new MaintenanceEmployeeResolver(context));
        var organizationId = Guid.NewGuid();
        var material = new MaintenanceDetail { Name = "Паста", MaterialName = "Паста", MaterialQuantity = 1 };
        var noWork = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rules.ValidateServiceDetailsAsync(organizationId, [material]));
        Assert.Contains("хотя бы одну работу", noWork.Message);
        material.CatalogItemId = Guid.NewGuid();
        var mixed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rules.ValidateServiceDetailsAsync(organizationId, [new MaintenanceDetail { Name = "Полировка" }, material]));
        Assert.Contains("отдельными строками", mixed.Message);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}
