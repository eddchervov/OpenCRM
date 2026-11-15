using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance;

namespace OpenCRM.Modules.Maintenance.Tests.Unit;

public sealed class MaintenanceReadRulesTests
{
    [Theory]
    [InlineData(0, MaintenancePaymentStatus.NoPaymentRequired)]
    [InlineData(200, "overpaid")]
    [InlineData(-20, "unpaid")]
    public void CancelledMaintenanceKeepsEstimateAndPaymentsButHasNoDebt(decimal paidAmount, string expectedStatus)
    {
        var maintenance = new MaintenanceEntity
        {
            Status = MaintenanceStatus.Cancelled,
            Details = [new MaintenanceDetail { Name = "Работа", Price = 1000m }],
            DiscountAmount = 100m,
            PaidAmount = paidAmount,
            MaterialCostAmount = 20m,
            LaborCostAmount = 30m
        };

        var cancelled = MaintenanceReadModelRules.BuildFinanceSummary(maintenance);

        Assert.Equal(1000m, cancelled.GrossAmount);
        Assert.Equal(0m, cancelled.DebtAmount);
        Assert.Equal(-50m, cancelled.ProfitAmount);
        Assert.Equal(expectedStatus, cancelled.PaymentStatus);
        Assert.Equal(paidAmount, maintenance.PaidAmount);
        Assert.Equal(100m, maintenance.DiscountAmount);

        maintenance.Status = MaintenanceStatus.Planned;
        var reopened = MaintenanceReadModelRules.BuildFinanceSummary(maintenance);

        Assert.Equal(900m - paidAmount, reopened.DebtAmount);
        Assert.Equal(850m, reopened.ProfitAmount);
    }

    [Fact]
    public void NormalizeMaintenanceDateClearsTimeAndKindAndKeepsNull()
    {
        var value = new DateTime(2026, 10, 11, 23, 45, 12, DateTimeKind.Utc);

        Assert.Null(MaintenanceReadModelRules.NormalizeMaintenanceDate(null));

        var normalized = MaintenanceReadModelRules.NormalizeMaintenanceDate(value);

        Assert.Equal(new DateTime(2026, 10, 11), normalized);
        Assert.Equal(DateTimeKind.Unspecified, normalized.Kind);
        Assert.Equal(normalized, MaintenanceReadModelRules.NormalizeMaintenanceDate((DateTime?)value));
    }

    [Theory]
    [InlineData(0, 100, 60, "unpaid")]
    [InlineData(40, 60, 60, "partial")]
    [InlineData(100, 0, 60, "paid")]
    [InlineData(125, 0, 60, "overpaid")]
    public void BuildFinanceSummaryCalculatesDebtAndPaymentState(
        decimal paidAmount,
        decimal expectedDebt,
        decimal expectedProfit,
        string expectedPaymentStatus)
    {
        var maintenance = new MaintenanceEntity
        {
            Details =
            [
                new MaintenanceDetail { Name = "Округление вниз", Price = 49.994m },
                new MaintenanceDetail { Name = "Округление вверх", Price = 50.005m },
                new MaintenanceDetail { Name = "Без цены", Price = null }
            ],
            DiscountAmount = 0,
            PaidAmount = paidAmount,
            MaterialCostAmount = 15,
            LaborCostAmount = 25
        };

        var result = MaintenanceReadModelRules.BuildFinanceSummary(maintenance);

        Assert.Equal(100m, result.GrossAmount);
        Assert.Equal(expectedDebt, result.DebtAmount);
        Assert.Equal(expectedProfit, result.ProfitAmount);
        Assert.Equal(expectedPaymentStatus, result.PaymentStatus);
    }

    [Fact]
    public void BuildFinanceSummaryDoesNotReturnNegativeNetOrDebtAndAllowsNegativeProfit()
    {
        var maintenance = new MaintenanceEntity
        {
            Details = [new MaintenanceDetail { Name = "Работа", Price = 10 }],
            DiscountAmount = 10,
            PaidAmount = 0,
            MaterialCostAmount = 20,
            LaborCostAmount = 5
        };

        var result = MaintenanceReadModelRules.BuildFinanceSummary(maintenance);

        Assert.Equal(10m, result.GrossAmount);
        Assert.Equal(0m, result.DebtAmount);
        Assert.Equal(-25m, result.ProfitAmount);
        Assert.Equal(MaintenancePaymentStatus.NoPaymentRequired, result.PaymentStatus);
    }

    [Fact]
    public void BuildFinanceSummaryKeepsUnpricedAndEmptyZeroOrdersUnpaid()
    {
        var unpriced = new MaintenanceEntity { Details = [new MaintenanceDetail { Name = "Работа" }] };
        var empty = new MaintenanceEntity { Details = [] };

        Assert.Equal("unpaid", MaintenanceReadModelRules.BuildFinanceSummary(unpriced).PaymentStatus);
        Assert.Equal("unpaid", MaintenanceReadModelRules.BuildFinanceSummary(empty).PaymentStatus);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData("  пост 1 ", "ПОСТ 1")]
    public void NormalizeSlotLocationTrimsAndUsesInvariantCase(string? location, string expected)
    {
        Assert.Equal(expected, MaintenanceSlotRules.NormalizeSlotLocation(location));
    }

    [Fact]
    public void PlannedIntervalUsesDateAndTimeFallbacksAndRepairsNonPositiveDuration()
    {
        var date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        var dateFallback = new MaintenanceEntity { Date = date, PlannedStartTime = null };
        var plannedDate = new MaintenanceEntity
        {
            Date = date.AddDays(-2),
            PlannedDate = date,
            PlannedStartTime = TimeSpan.FromHours(9),
            PlannedEndTime = null
        };
        var crossDay = new MaintenanceEntity
        {
            Date = date,
            PlannedDate = date,
            PlannedStartTime = TimeSpan.FromHours(22),
            PlannedEndDate = date.AddDays(1),
            PlannedEndTime = TimeSpan.FromHours(2)
        };
        var invalidEnd = new MaintenanceEntity
        {
            Date = date,
            PlannedStartTime = TimeSpan.FromHours(10),
            PlannedEndTime = TimeSpan.FromHours(10)
        };

        Assert.Equal(date, MaintenanceSlotRules.GetPlannedStart(dateFallback));
        Assert.Equal(date.AddHours(9), MaintenanceSlotRules.GetPlannedStart(plannedDate));
        Assert.Equal(date.AddHours(10), MaintenanceSlotRules.GetPlannedEnd(plannedDate));
        Assert.Equal(date.AddDays(1).AddHours(2), MaintenanceSlotRules.GetPlannedEnd(crossDay));
        Assert.Equal(date.AddHours(11), MaintenanceSlotRules.GetPlannedEnd(invalidEnd));
    }
}
