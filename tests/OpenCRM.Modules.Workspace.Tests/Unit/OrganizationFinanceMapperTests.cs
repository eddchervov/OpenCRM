using OpenCRM.Modules.Workspace.Queries;

using OpenCRM.Domain;

namespace OpenCRM.Modules.Workspace.Tests.Unit;

public sealed class OrganizationFinanceMapperTests
{
    [Theory]
    [InlineData(10.125, 10.13)]
    [InlineData(-10.125, -10.13)]
    [InlineData(10.124, 10.12)]
    [InlineData(0, 0)]
    public void RoundMoneyUsesAwayFromZero(decimal value, decimal expected)
    {
        Assert.Equal(expected, OrganizationFinanceMapper.RoundMoney(value));
    }

    [Theory]
    [InlineData(-1, 100, "unpaid")]
    [InlineData(0, 100, "unpaid")]
    [InlineData(0, 0, "unpaid")]
    [InlineData(99, 100, "partial")]
    [InlineData(100, 100, "paid")]
    [InlineData(101, 100, "overpaid")]
    public void ResolvePaymentStatusCoversBalanceBoundaries(decimal paid, decimal net, string expected)
    {
        Assert.Equal(expected, OrganizationFinanceMapper.ResolvePaymentStatus(paid, net));
    }

    [Fact]
    public void ResolvePaymentStatusMarksFullyPricedZeroOrdersAsNotRequiringPayment()
    {
        Assert.Equal(MaintenancePaymentStatus.NoPaymentRequired,
            OrganizationFinanceMapper.ResolvePaymentStatus(0m, 0m, hasPricedDetail: true, areAllDetailsPriced: true));
        Assert.Equal("unpaid",
            OrganizationFinanceMapper.ResolvePaymentStatus(0m, 0m, hasPricedDetail: false, areAllDetailsPriced: true));
        Assert.Equal("unpaid",
            OrganizationFinanceMapper.ResolvePaymentStatus(0m, 0m, hasPricedDetail: true, areAllDetailsPriced: false));
        Assert.Equal("overpaid",
            OrganizationFinanceMapper.ResolvePaymentStatus(1m, 0m, hasPricedDetail: true, areAllDetailsPriced: true));
    }

    [Theory]
    [InlineData("unpaid", "Не оплачено")]
    [InlineData("partial", "Частично")]
    [InlineData("paid", "Оплачено")]
    [InlineData("overpaid", "Переплата")]
    [InlineData("no_payment_required", "Оплата не требуется")]
    [InlineData("unexpected", "Неизвестно")]
    public void ResolvePaymentStatusNameMapsKnownAndUnknownValues(string status, string expected)
    {
        Assert.Equal(expected, OrganizationFinanceMapper.ResolvePaymentStatusName(status));
    }

    [Fact]
    public void BuildMoneyBreakdownOmitsNonPositiveItemsAndRoundsPercentages()
    {
        var result = OrganizationFinanceMapper.BuildMoneyBreakdown(
        [
            ("first", "Первый", 1m),
            ("second", "Второй", 2m),
            ("zero", "Нулевой", 0m),
            ("negative", "Отрицательный", -1m)
        ]);

        Assert.Equal(2, result.Count);
        Assert.Collection(result,
            first =>
            {
                Assert.Equal("first", first.Code);
                Assert.Equal(50, first.Percent);
            },
            second =>
            {
                Assert.Equal("second", second.Code);
                Assert.Equal(100, second.Percent);
            });
    }

    [Fact]
    public void BuildMoneyBreakdownReturnsNoItemsWhenTotalIsNotPositive()
    {
        Assert.Empty(OrganizationFinanceMapper.BuildMoneyBreakdown(
        [
            ("zero", "Нулевой", 0m),
            ("negative", "Отрицательный", -1m)
        ]));
    }

    [Fact]
    public void BuildMoneyBreakdownUsesZeroPercentWhenPositiveItemsHaveANonPositiveNetTotal()
    {
        var result = Assert.Single(OrganizationFinanceMapper.BuildMoneyBreakdown(
        [
            ("positive", "Положительное", 10m),
            ("negative", "Отрицательное", -20m)
        ]));

        Assert.Equal("positive", result.Code);
        Assert.Equal(0, result.Percent);
    }

    [Fact]
    public void EachDayIncludesBothBoundsAndReturnsEmptyForReversedBounds()
    {
        var from = new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc);
        var dates = OrganizationFinanceMapper.EachDay(from, from.AddDays(2)).ToArray();

        Assert.Equal(new[] { from.Date, from.Date.AddDays(1), from.Date.AddDays(2) }, dates);
        Assert.Empty(OrganizationFinanceMapper.EachDay(from.AddDays(1), from));
    }
}
