using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Workspace.Queries;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Testing;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class OrganizationFinanceAggregationTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Theory]
    [InlineData("en-US", DateTimeKind.Utc, "UTC")]
    [InlineData("tr-TR", DateTimeKind.Unspecified, "Pacific/Auckland")]
    [InlineData("ru-RU", DateTimeKind.Local, "America/New_York")]
    public async Task DatabaseAggregatesMatchOriginalFinanceAcrossDateKindsNamesBuyersAndMoreThanEightyOrders(
        string cultureName,
        DateTimeKind dateKind,
        string sessionTimeZone)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        try
        {
            await using var context = fixture.CreateContext();
            var user = new ApplicationUser();
            var organization = new Organization { Name = "Финансовые агрегаты", OwnerUser = user };
            var owner = new OrganizationClient { Organization = organization, Name = "Владелец техники", IsDeleted = true };
            var buyer = new OrganizationClient { Organization = organization, Name = "Текущее имя покупателя" };
            var secondBuyer = new OrganizationClient { Organization = organization, Name = "Другой покупатель" };
            var brand = new Brand { Name = $"Volvo-{cultureName}" };
            var model = new Model { Brand = brand, Name = "XC90" };
            var vehicles = new[]
            {
                OrganizationFinanceAggregationTests.Vehicle(user, organization, owner),
                OrganizationFinanceAggregationTests.Vehicle(user, organization, owner),
                OrganizationFinanceAggregationTests.Vehicle(user, organization, owner),
                OrganizationFinanceAggregationTests.Vehicle(user, organization, owner)
            };

            vehicles[1].Brand = brand;
            vehicles[1].Model = model;
            vehicles[2].Model = model;
            vehicles[3].Brand = brand;
            var date = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);
            var names = new[]
            {
                " Полировка ", "полировка", "\u00a0ПОЛИРОВКА\u00a0", "I", "ı", "i", "İ",
                "Σ", "σ", "ς", "K", "K", "k", "ß", "SS", "\u2003\u2003", "Покрытие"
            };

            decimal?[] prices = [0m, 0.01m, 1.01m, 2.99m, 10.12m, -10.12m, null, 999999999m, 100.01m];
            var orders = new List<MaintenanceEntity>();
            for (var index = 0; index < 112; index++)
            {
                var effectiveDate = date.AddDays(index % 3).AddMinutes(index);
                var order = OrganizationFinanceAggregationTests.Order(vehicles[index % vehicles.Length], effectiveDate);
                order.Status = (MaintenanceStatus)(index % 4 + 1);
                order.Date = effectiveDate.AddDays(-5);
                order.CompletedDate = index % 4 == 0 || index % 4 == 3 ? effectiveDate : null;
                order.PlannedDate = index % 4 == 2 ? null : effectiveDate;
                if (index % 4 == 2)
                {
                    order.Date = effectiveDate;
                }

                order.PaidAmount = index % 7 == 0 ? -5m : index % 7 == 1 ? 0m : index % 7 == 2 ? 1000000000m : 5m;
                order.DiscountAmount = index % 5 == 0 ? 1000m : index % 5 == 1 ? -0.01m : 1.25m;
                order.MaterialCostAmount = index % 6 == 0 ? -1.25m : 10.01m;
                order.LaborCostAmount = index % 8 == 0 ? -3m : 12.99m;
                order.BuyerClientId = index % 4 == 0 ? null : index % 4 == 1 ? buyer.Id : secondBuyer.Id;
                order.BuyerName = (index % 5) switch
                {
                    0 => "Снимок без связи",
                    1 => " Клиент ",
                    2 => "Клиент",
                    3 => "\u00a0Клиент\u00a0",
                    _ => "\u2003\u2003"
                };
                order.Details = index % 9 == 0
                    ? []
                    : [
                        new() { Name = names[index % names.Length], Price = prices[index % prices.Length] },
                        new() { Name = names[(index + 1) % names.Length], Price = prices[(index + 3) % prices.Length] },
                        new() { Name = "Материал", MaterialName = "Состав", Price = 0.01m }
                    ];
                var details = order.Details;
                if (details.Count > 0)
                {
                    details[0].Position = 20;
                    details[1].Position = 10;
                }

                orders.Add(order);
            }

            orders[0].Id = Guid.Parse($"00000001-{Guid.NewGuid().ToString()[9..]}");
            orders[3].Id = Guid.Parse($"ffffffff-{Guid.NewGuid().ToString()[9..]}");
            var deletedOrder = OrganizationFinanceAggregationTests.Order(vehicles[0], date);
            deletedOrder.IsDeleted = true;
            var deletedVehicle = OrganizationFinanceAggregationTests.Vehicle(user, organization, owner);
            deletedVehicle.IsDeleted = true;
            var foreignUser = new ApplicationUser();
            var foreignOrganization = new Organization { Name = "Другая организация", OwnerUser = foreignUser };
            var foreignVehicle = OrganizationFinanceAggregationTests.Vehicle(foreignUser, foreignOrganization, null);
            context.OrganizationClients.AddRange(buyer, secondBuyer);
            context.Maintenance.AddRange(orders);
            context.Maintenance.AddRange(deletedOrder,
                OrganizationFinanceAggregationTests.Order(deletedVehicle, date),
                OrganizationFinanceAggregationTests.Order(foreignVehicle, date),
                OrganizationFinanceAggregationTests.Order(vehicles[0], date.AddDays(-2)),
                OrganizationFinanceAggregationTests.Order(vehicles[0], date.AddDays(3)));
            await context.SaveChangesAsync();
            await context.Database.OpenConnectionAsync();
            try
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('TimeZone', {sessionTimeZone}, false)");
                var from = DateTime.SpecifyKind(date.AddHours(14), dateKind);
                var to = DateTime.SpecifyKind(date.AddDays(2).AddHours(23), dateKind);
                var expected = await new FinanceQueryOracle(context).GetFinanceSummaryAsync(organization.Id, to, from);
                var actual = await new OrganizationFinanceQueryService(context).GetFinanceSummaryAsync(organization.Id, to, from);

                OrganizationFinanceAggregationTests.AssertMatchesOracle(expected, actual);
                Assert.True(actual.Kpis.OrdersCount > 80);
                Assert.Equal(80, actual.Orders.Count);
                Assert.DoesNotContain(actual.Orders, order => order.ClientId == null && order.ClientName != null);
                Assert.DoesNotContain(actual.TopDebtors, debtor => debtor.ClientName == owner.Name);
                Assert.True(actual.TopServices.Count <= 8);
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public async Task CancelledOrdersKeepPaidAndActualCostsButDoNotAddAccrualOrServiceRevenue()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Отменённые начисления", OwnerUser = user };
        var vehicle = OrganizationFinanceAggregationTests.Vehicle(user, organization, null);
        var date = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);
        var active = OrganizationFinanceAggregationTests.Order(vehicle, date);
        active.Details = [new() { Name = "Действующая услуга", Price = 1000m }];
        active.DiscountAmount = 100m;
        active.PaidAmount = 200m;
        active.MaterialCostAmount = 50m;
        active.LaborCostAmount = 20m;
        var cancelled = OrganizationFinanceAggregationTests.Order(vehicle, date.AddHours(1));
        cancelled.Status = MaintenanceStatus.Cancelled;
        cancelled.Details = [new() { Name = "Отменённая услуга", Price = 3000m }];
        cancelled.DiscountAmount = 200m;
        cancelled.PaidAmount = 500m;
        cancelled.MaterialCostAmount = 120m;
        cancelled.LaborCostAmount = 80m;
        context.Maintenance.AddRange(active, cancelled);
        await context.SaveChangesAsync();

        var expected = await new FinanceQueryOracle(context).GetFinanceSummaryAsync(organization.Id, date, date);
        var actual = await new OrganizationFinanceQueryService(context).GetFinanceSummaryAsync(organization.Id, date, date);

        OrganizationFinanceAggregationTests.AssertMatchesOracle(expected, actual);
        Assert.Equal(1, actual.Kpis.OrdersCount);
        Assert.Equal(1000m, actual.Kpis.GrossAmount);
        Assert.Equal(900m, actual.Kpis.NetAmount);
        Assert.Equal(700m, actual.Kpis.PaidAmount);
        Assert.Equal(700m, actual.Kpis.DebtAmount);
        Assert.Equal(100m, actual.Kpis.DiscountAmount);
        Assert.Equal(170m, actual.Kpis.MaterialCostAmount);
        Assert.Equal(100m, actual.Kpis.LaborCostAmount);
        Assert.Equal(630m, actual.Kpis.ProfitAmount);
        Assert.Equal(900m, actual.AccruedSeries.Single().Value);
        Assert.Equal("Действующая услуга", Assert.Single(actual.TopServices).Name);
        var cancelledItem = Assert.Single(actual.Orders, item => item.Id == cancelled.Id);
        Assert.True(cancelledItem.IsCancelled);
        Assert.Equal(3000m, cancelledItem.GrossAmount);
        Assert.Equal(200m, cancelledItem.DiscountAmount);
        Assert.Equal(0m, cancelledItem.NetAmount);
        Assert.Equal(500m, cancelledItem.PaidAmount);
        Assert.Equal(0m, cancelledItem.DebtAmount);

        cancelled.Details[0].Price = null;
        cancelled.PaidAmount = 0m;
        await context.SaveChangesAsync();
        var unpaidCancelled = await new OrganizationFinanceQueryService(context).GetFinanceSummaryAsync(organization.Id, date, date);
        Assert.Equal("no_payment_required", Assert.Single(unpaidCancelled.Orders, item => item.Id == cancelled.Id).PaymentStatus);
    }

    [Fact]
    public async Task AverageCheckUsesAwayFromZeroWhilePercentagesKeepToEvenAndEmptyPeriodsRemainFilled()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Округление финансов", OwnerUser = user };
        var vehicle = OrganizationFinanceAggregationTests.Vehicle(user, organization, null);
        var date = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);
        var first = OrganizationFinanceAggregationTests.Order(vehicle, date);
        first.MaterialCostAmount = 2m;
        first.LaborCostAmount = 6m;
        first.Details = [new() { Name = "Первая услуга", Price = 1m }];
        var second = OrganizationFinanceAggregationTests.Order(vehicle, date.AddHours(1));
        second.DiscountAmount = 8m;
        second.PaidAmount = 3m;
        second.Details = [new() { Name = "Вторая услуга", Price = 10.01m }];
        context.Maintenance.AddRange(first, second);
        await context.SaveChangesAsync();
        var service = new OrganizationFinanceQueryService(context);
        var expected = await new FinanceQueryOracle(context).GetFinanceSummaryAsync(organization.Id, date, date);
        var actual = await service.GetFinanceSummaryAsync(organization.Id, date, date);

        OrganizationFinanceAggregationTests.AssertMatchesOracle(expected, actual);
        Assert.Equal(1.51m, actual.Kpis.AverageCheckAmount);
        Assert.Equal(-4.99m, actual.Kpis.ProfitAmount);
        Assert.Equal(12, actual.CostBreakdown.Single(item => item.Code == "materials").Percent);
        Assert.Equal(38, actual.CostBreakdown.Single(item => item.Code == "labor").Percent);
        Assert.Equal("overpaid", actual.Orders.Single(item => item.Id == second.Id).PaymentStatus);
        var empty = await service.GetFinanceSummaryAsync(organization.Id, date.AddDays(1), date.AddDays(3));
        var expectedEmpty = await new FinanceQueryOracle(context).GetFinanceSummaryAsync(organization.Id, date.AddDays(1), date.AddDays(3));

        OrganizationFinanceAggregationTests.AssertMatchesOracle(expectedEmpty, empty);
        Assert.Equal(3, empty.RevenueSeries.Count);
        Assert.All(empty.RevenueSeries, item => Assert.Equal(0m, item.Value));
        Assert.Empty(empty.Orders);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1900)]
    [InlineData(9999)]
    public async Task FirstServiceNameKeepsDateGuidAndDetailPositionOrderAcrossSupportedYears(int year)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Порядок финансовых групп", OwnerUser = user };
        var vehicle = OrganizationFinanceAggregationTests.Vehicle(user, organization, null);
        vehicle.YearIssue = 1999;
        var date = new DateTime(year, 2, 2, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234560);
        var earlier = OrganizationFinanceAggregationTests.Order(vehicle, date.AddDays(-1));
        earlier.Id = Guid.Parse($"00000000-{Guid.NewGuid().ToString()[9..]}");
        earlier.Details = [new() { Name = "ПОЛИРОВКА", Price = 10m }];
        var first = OrganizationFinanceAggregationTests.Order(vehicle, date);
        first.Id = Guid.Parse($"00000001-{Guid.NewGuid().ToString()[9..]}");
        first.Details =
        [
            new() { Name = " Полировка ", Price = 20m },
            new() { Name = "\u00a0полировка\u00a0", Price = 30m }
        ];

        var details = first.Details;
        details[0].Position = 20;
        details[1].Position = 10;
        var last = OrganizationFinanceAggregationTests.Order(vehicle, date);
        last.Id = Guid.Parse($"ffffffff-{Guid.NewGuid().ToString()[9..]}");
        last.Details = [new() { Name = "ПоЛиРоВкА", Price = 40m }];
        context.Maintenance.AddRange(last, first, earlier);
        await context.SaveChangesAsync();
        var expected = await new FinanceQueryOracle(context).GetFinanceSummaryAsync(organization.Id, date.AddDays(-1), date);
        var actual = await new OrganizationFinanceQueryService(context).GetFinanceSummaryAsync(organization.Id, date.AddDays(-1), date);

        OrganizationFinanceAggregationTests.AssertMatchesOracle(expected, actual);
        var service = Assert.Single(actual.TopServices);
        Assert.Equal("полировка", service.Name);
        Assert.Equal(4, service.Count);
        Assert.Equal(100m, service.Amount);
    }

    [Fact]
    public async Task NullZeroAndMissingDetailsKeepAmountsAndNegativePaymentsKeepClampedDebt()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Нулевые финансовые итоги", OwnerUser = user };
        var vehicle = OrganizationFinanceAggregationTests.Vehicle(user, organization, null);
        var date = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);
        var nullPrice = OrganizationFinanceAggregationTests.Order(vehicle, date);
        nullPrice.Details = [new() { Name = "Без цены", Price = null }];
        var zeroPrice = OrganizationFinanceAggregationTests.Order(vehicle, date);
        zeroPrice.Details = [new() { Name = "Нулевая цена", Price = 0m }];
        var noDetails = OrganizationFinanceAggregationTests.Order(vehicle, date);
        noDetails.Details = [];
        var negative = OrganizationFinanceAggregationTests.Order(vehicle, date);
        negative.Details = [new() { Name = "Отрицательная цена", Price = -2m }];
        negative.DiscountAmount = -1m;
        negative.PaidAmount = -1m;
        context.Maintenance.AddRange(nullPrice, zeroPrice, noDetails, negative);
        await context.SaveChangesAsync();
        var expected = await new FinanceQueryOracle(context).GetFinanceSummaryAsync(organization.Id, date, date);
        var actual = await new OrganizationFinanceQueryService(context).GetFinanceSummaryAsync(organization.Id, date, date);

        OrganizationFinanceAggregationTests.AssertMatchesOracle(expected, actual);
        Assert.Equal(-2m, actual.Kpis.GrossAmount);
        Assert.Equal(0m, actual.Kpis.NetAmount);
        Assert.Equal(1m, actual.Kpis.DebtAmount);
        Assert.Empty(actual.TopServices);
    }

    [Fact]
    public async Task MinimumDateMatchesOriginalForNpgsqlInfinityAndFiniteFirstDayValues()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Минимальная дата финансов", OwnerUser = user };
        var vehicle = OrganizationFinanceAggregationTests.Vehicle(user, organization, null);
        var date = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        var minimum = OrganizationFinanceAggregationTests.Order(vehicle, date);
        minimum.Id = Guid.Parse($"ffffffff-{Guid.NewGuid().ToString()[9..]}");
        minimum.Details = [new() { Name = "ПОЛИРОВКА", Price = 10m }];
        var finite = OrganizationFinanceAggregationTests.Order(vehicle, date.AddHours(1));
        finite.Id = Guid.Parse($"00000001-{Guid.NewGuid().ToString()[9..]}");
        finite.Details = [new() { Name = " полировка ", Price = 20m }];
        var nextDay = OrganizationFinanceAggregationTests.Order(vehicle, date.AddDays(1));
        nextDay.Details = [new() { Name = "Другая услуга", Price = 30m }];
        context.Maintenance.AddRange(minimum, finite, nextDay);
        await context.SaveChangesAsync();
        var expected = await new FinanceQueryOracle(context).GetFinanceSummaryAsync(organization.Id, date, date.AddDays(1));
        var actual = await new OrganizationFinanceQueryService(context).GetFinanceSummaryAsync(organization.Id, date, date.AddDays(1));

        OrganizationFinanceAggregationTests.AssertMatchesOracle(expected, actual);
        Assert.Equal(3, actual.Kpis.OrdersCount);
        Assert.Equal("полировка", actual.TopServices.Single(service => service.Count == 2).Name);
        Assert.Equal(minimum.Id, actual.Orders[1].Id);
        Assert.Equal(date, actual.Orders[1].Date);
    }

    [Theory]
    [InlineData(1900)]
    [InlineData(2026)]
    public async Task LocalPeriodBoundariesAndDetailOrderMatchOriginal(int year)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Границы финансового периода", OwnerUser = user };
        var vehicle = OrganizationFinanceAggregationTests.Vehicle(user, organization, null);
        var date = new DateTime(year, 2, 2, 0, 0, 0, DateTimeKind.Local);
        var fromUtc = date.ToUniversalTime();
        var toUtc = date.AddDays(1).ToUniversalTime();
        var dates = new[]
        {
            fromUtc.AddTicks(-10), fromUtc, fromUtc.AddMinutes(1), fromUtc.AddHours(1),
            toUtc.AddMinutes(-1), toUtc.AddTicks(-10), toUtc
        };

        var orders = dates.Select(orderDate => OrganizationFinanceAggregationTests.Order(vehicle, orderDate)).ToList();
        foreach (var order in orders)
        {
            order.PaidAmount = -1m;
            order.Details =
            [
                new() { DetailId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), Name = " ПОЛИРОВКА ", Price = 2m },
                new() { DetailId = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = " полировка ", Price = 3m }
            ];

            var details = order.Details;
            details[0].Position = 1;
            details[1].Position = 0;
        }

        context.Maintenance.AddRange(orders);
        await context.SaveChangesAsync();
        var expected = await new FinanceQueryOracle(context).GetFinanceSummaryAsync(organization.Id, date, date);
        var actual = await new OrganizationFinanceQueryService(context).GetFinanceSummaryAsync(organization.Id, date, date);

        OrganizationFinanceAggregationTests.AssertMatchesOracle(expected, actual);
        Assert.Equal(5, actual.Kpis.OrdersCount);
        Assert.Equal(30m, actual.Kpis.DebtAmount);
        Assert.Equal("полировка", Assert.Single(actual.TopServices).Name);
    }

    [Fact]
    public async Task FiniteFirstDayAndNegativeInfinityKeepAllRawServiceGroupsAndGuidOrder()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Группы минимальной даты", OwnerUser = user };
        var vehicle = OrganizationFinanceAggregationTests.Vehicle(user, organization, null);
        var date = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        var infinite = OrganizationFinanceAggregationTests.Order(vehicle, date);
        infinite.Id = Guid.Parse($"ffffffff-{Guid.NewGuid().ToString()[9..]}");
        infinite.Details = [new() { Name = "ПОЛИРОВКА", Price = 10m }];
        var finite = OrganizationFinanceAggregationTests.Order(vehicle, date);
        finite.Id = Guid.Parse($"00000001-{Guid.NewGuid().ToString()[9..]}");
        finite.Details = [new() { Name = " полировка ", Price = 20m }];
        context.Maintenance.AddRange(infinite, finite);
        await context.SaveChangesAsync();

        // Конечное начало первого года и -infinity читаются Npgsql как одна дата, но хранятся по-разному.
        await context.Database.ExecuteSqlAsync($"""
            UPDATE "Maintenance" SET "CompletedDate" = TIMESTAMPTZ '0001-01-01 00:00:00+00' WHERE "Id" = {finite.Id};
            UPDATE "MaintenanceFinancialTotals" SET "EffectiveDate" = TIMESTAMPTZ '0001-01-01 00:00:00+00'
                WHERE "MaintenanceId" = {finite.Id};
            """);
        context.ChangeTracker.Clear();
        var expected = await new FinanceQueryOracle(context).GetFinanceSummaryAsync(organization.Id, date, date);
        var actual = await new OrganizationFinanceQueryService(context).GetFinanceSummaryAsync(organization.Id, date, date);

        OrganizationFinanceAggregationTests.AssertMatchesOracle(expected, actual);
        var service = Assert.Single(actual.TopServices);
        Assert.Equal("полировка", service.Name);
        Assert.Equal(2, service.Count);
        Assert.Equal(30m, service.Amount);
    }

    [Theory]
    [InlineData(2026, 330, 0, 1440)]
    [InlineData(1900, 331, 40, 1440)]
    [InlineData(2026, 330, 0, 10)]
    [InlineData(1, 0, 0, 10)]
    [InlineData(1, 0, -1, 10)]
    public async Task HourlyServiceTotalsMatchSourceAcrossPartialHoursAndHistoricalOffsets(
        int year,
        int offsetMinutes,
        int offsetSeconds,
        int durationMinutes)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Частичные часы услуг", OwnerUser = user };
        var vehicle = OrganizationFinanceAggregationTests.Vehicle(user, organization, null);
        var date = new DateTime(year, year == 1 ? 1 : 2, year == 1 ? 1 : 2, 0, 0, 0, DateTimeKind.Utc);
        var fromUtc = date.AddMinutes(-offsetMinutes).AddSeconds(-offsetSeconds);
        var toUtc = fromUtc.AddMinutes(durationMinutes);
        var dates = new[]
        {
            fromUtc.Ticks == 0 ? fromUtc : fromUtc.AddTicks(-10), fromUtc,
            fromUtc.AddTicks(10), fromUtc.AddMinutes(1), fromUtc.AddHours(1),
            toUtc.AddMinutes(-1), toUtc.AddTicks(-10), toUtc
        };

        var longName = new string(Enumerable.Range(0, 1000).Select(index => (char)(0x4E00 + index)).ToArray());
        var orders = dates.Select(orderDate => OrganizationFinanceAggregationTests.Order(vehicle, orderDate)).ToList();
        for (var index = 0; index < orders.Count; index++)
        {
            orders[index].Details =
            [
                new() { Name = index % 2 == 0 ? " ПОЛИРОВКА " : "\u00a0полировка\u00a0", Price = 2m },
                new() { Name = "полировка", Price = 3.25m },
                new() { Name = longName, Price = 4.01m },
                new() { Name = "\u2003\u2003", Price = 5m },
                new() { Name = "Отрицательная строка", Price = -1m }
            ];
        }

        context.Maintenance.AddRange(orders);
        await context.SaveChangesAsync();
        var expected = orders
            .Where(order => order.CompletedDate >= fromUtc && order.CompletedDate < toUtc)
            .OrderByDescending(order => order.CompletedDate)
            .ThenBy(order => order.Id)
            .SelectMany(order => order.Details.OrderBy(detail => detail.Position).ThenBy(detail => detail.DetailId))
            .Where(detail => detail.Price > 0 && !string.IsNullOrWhiteSpace(detail.Name))
            .GroupBy(detail => detail.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new OrganizationFinanceServiceItemDto
            {
                Name = group.Key,
                Count = group.Count(),
                Amount = group.Sum(detail => detail.Price!.Value)
            })
            .OrderByDescending(item => item.Amount)
            .ThenBy(item => item.Name)
            .Take(8)
            .ToList();

        var actual = await new OrganizationFinanceQueryService(context).GetFinanceSummaryAsync(
            organization.Id, date.Date, date.Date, fromUtc, toUtc);
        var expectedJson = JsonSerializer.SerializeToNode(expected);
        var actualJson = JsonSerializer.SerializeToNode(actual.TopServices);

        Assert.True(JsonNode.DeepEquals(expectedJson, actualJson), $"Ожидалось: {expectedJson}\nПолучено: {actualJson}");
        Assert.Contains(actual.TopServices, service => service.Name == longName);
    }

    private static void AssertMatchesOracle(OrganizationFinanceSummaryDto expected, OrganizationFinanceSummaryDto actual)
    {
        var expectedJson = JsonSerializer.SerializeToNode(expected);
        var actualJson = JsonSerializer.SerializeToNode(actual);

        Assert.True(JsonNode.DeepEquals(expectedJson, actualJson), $"Ожидалось: {expectedJson}\nПолучено: {actualJson}");
    }

    private static Vehicle Vehicle(ApplicationUser user, Organization organization, OrganizationClient? owner) => new()
    {
        User = user,
        Organization = organization,
        OrganizationClient = owner,
        ManualBrandName = "BMW",
        ManualModelName = "X3",
        RegistrationPlate = "А123АА154",
        Name = "Название техники",
        YearIssue = 2020
    };

    private static MaintenanceEntity Order(Vehicle vehicle, DateTime date) => new()
    {
        User = vehicle.User,
        Vehicle = vehicle,
        Status = MaintenanceStatus.Completed,
        Date = date,
        CompletedDate = date,
        DateTimeCreation = date,
        Details = [new() { Name = "Услуга", Price = 100m }]
    };
}
