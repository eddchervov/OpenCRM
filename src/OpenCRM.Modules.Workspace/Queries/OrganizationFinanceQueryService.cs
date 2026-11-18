using System.Text.Json;
using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Workspace.Queries;

/// <summary> Сервис финансовых сводок организации. </summary>
public sealed class OrganizationFinanceQueryService(OpenCRMContext opencrmContext) : IOrganizationFinanceQueryService
{
    /// <summary> Возвращает финансовую сводку организации за период. </summary>
    public async Task<OrganizationFinanceSummaryDto> GetFinanceSummaryAsync(
        Guid organizationId,
        DateTime dateFrom,
        DateTime dateTo)
    {
        var from = dateFrom.Date;
        var to = dateTo.Date;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var toExclusive = to.AddDays(1);
        var fromUtc = from.Kind == DateTimeKind.Utc ? from : from.ToUniversalTime();
        var toUtc = toExclusive.Kind == DateTimeKind.Utc ? toExclusive : toExclusive.ToUniversalTime();

        return await this.GetFinanceSummaryAsync(organizationId, from, to, fromUtc, toUtc);
    }

    internal async Task<OrganizationFinanceSummaryDto> GetFinanceSummaryAsync(
        Guid organizationId,
        DateTime from,
        DateTime to,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var currencyCode = await opencrmContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.CurrencyCode)
            .SingleAsync();
        var firstUtcDay = (int)(fromUtc.Ticks / TimeSpan.TicksPerDay);
        var lastUtcDay = (int)((toUtc.Ticks - 1) / TimeSpan.TicksPerDay);

        var firstCompleteHour = (int)((fromUtc.Ticks + TimeSpan.TicksPerHour - 1) / TimeSpan.TicksPerHour);
        var lastCompleteHour = (int)(toUtc.Ticks / TimeSpan.TicksPerHour);
        var completeFromUtc = new DateTime(firstCompleteHour * TimeSpan.TicksPerHour, DateTimeKind.Utc);
        var completeToUtc = new DateTime(lastCompleteHour * TimeSpan.TicksPerHour, DateTimeKind.Utc);
        var hasCompleteHours = firstCompleteHour < lastCompleteHour;
        var hasPartialHours = !hasCompleteHours || fromUtc != completeFromUtc || toUtc != completeToUtc;

        // Все части сводки читают один снимок данных; финансовые суммы берутся из проекции заказа.
        var json = await opencrmContext.Database.SqlQuery<string>($"""
            WITH financial_groups AS MATERIALIZED (
                SELECT grouping("UtcDay") AS "GroupKind", "UtcDay", count(*) FILTER (WHERE "IsAccruable")::integer AS "OrdersCount",
                    COALESCE(sum("GrossAmount") FILTER (WHERE "IsAccruable"), 0) AS "GrossAmount",
                    COALESCE(sum("NetAmount") FILTER (WHERE "IsAccruable"), 0) AS "NetAmount",
                    COALESCE(sum("PaidAmount"), 0) AS "PaidAmount", COALESCE(sum("DebtAmount"), 0) AS "DebtAmount",
                    COALESCE(sum("DiscountAmount") FILTER (WHERE "IsAccruable"), 0) AS "DiscountAmount",
                    COALESCE(sum("MaterialCostAmount"), 0) AS "MaterialCostAmount",
                    COALESCE(sum("LaborCostAmount"), 0) AS "LaborCostAmount",
                    COALESCE(sum("MaterialCostAmount" + "LaborCostAmount"), 0) AS "CostAmount",
                    COALESCE(sum("ProfitAmount"), 0) AS "ProfitAmount"
                FROM "MaintenanceFinancialTotals"
                WHERE "OrganizationId" = {organizationId} AND "IsVisible"
                    AND "CurrencyCode" = {currencyCode}
                    AND "EffectiveDate" >= {fromUtc} AND "EffectiveDate" < {toUtc}
                GROUP BY GROUPING SETS ((), ("UtcDay"))
            ), debtor_groups AS NOT MATERIALIZED (
                SELECT "ClientId", "ClientName" COLLATE "C" AS "Name", count(*)::integer AS "Count",
                    sum("DebtAmount") AS "Amount", min("FirstKey" COLLATE "C") AS "FirstKey"
                FROM "MaintenanceFinancialTotals"
                WHERE "OrganizationId" = {organizationId} AND "IsVisible" AND "IsAccruable" AND "DebtAmount" > 0
                    AND "CurrencyCode" = {currencyCode}
                    AND "EffectiveDate" >= {fromUtc} AND "EffectiveDate" < {toUtc}
                GROUP BY "ClientId", "ClientName" COLLATE "C"
            ), service_edges AS NOT MATERIALIZED (
                SELECT {fromUtc} AS "From", {toUtc} AS "To"
                WHERE {hasPartialHours} AND NOT {hasCompleteHours}
                UNION ALL
                SELECT {fromUtc}, {completeFromUtc}
                WHERE {hasCompleteHours} AND {fromUtc} < {completeFromUtc}
                UNION ALL
                SELECT {completeToUtc}, {toUtc}
                WHERE {hasCompleteHours} AND {completeToUtc} < {toUtc}
            ), service_inputs AS NOT MATERIALIZED (
                SELECT "Name", "Count", "Amount", "FirstKey"
                FROM "OrganizationServiceHourlyTotals"
                WHERE "OrganizationId" = {organizationId}
                    AND "CurrencyCode" = {currencyCode}
                    AND "UtcHour" >= {firstCompleteHour} AND "UtcHour" < {lastCompleteHour}
                UNION ALL
                SELECT detail."Name", 1::bigint, detail."Price",
                    total."FirstKey" || ':' || lpad(detail."Position"::text, 10, '0') || ':' || detail."DetailId"::text
                FROM service_edges AS edge
                CROSS JOIN LATERAL (
                    SELECT "MaintenanceId", "FirstKey" FROM "MaintenanceFinancialTotals"
                    WHERE "OrganizationId" = {organizationId} AND "IsVisible" AND "IsAccruable"
                        AND "CurrencyCode" = {currencyCode}
                        AND "EffectiveDate" >= edge."From" AND "EffectiveDate" < edge."To" OFFSET 0
                ) AS total
                CROSS JOIN LATERAL (
                    SELECT "Name", "Price", "Position", "DetailId" FROM "MaintenanceDetails"
                    WHERE "MaintenanceId" = total."MaintenanceId" AND "Price" > 0 AND "Name" IS NOT NULL OFFSET 0
                ) AS detail
            ), service_groups AS NOT MATERIALIZED (
                SELECT "Name" COLLATE "C" AS "Name", sum("Count")::integer AS "Count", sum("Amount") AS "Amount",
                    min("FirstKey" COLLATE "C") AS "FirstKey"
                FROM service_inputs GROUP BY "Name" COLLATE "C"
            ), other_currency_totals AS MATERIALIZED (
                SELECT "CurrencyCode", COALESCE(SUM("GrossAmount") FILTER (WHERE "IsAccruable"), 0) AS "GrossAmount",
                    COALESCE(SUM("NetAmount") FILTER (WHERE "IsAccruable"), 0) AS "NetAmount",
                    COALESCE(SUM("PaidAmount"), 0) AS "PaidAmount", COALESCE(SUM("DebtAmount"), 0) AS "DebtAmount",
                    COALESCE(SUM("DiscountAmount") FILTER (WHERE "IsAccruable"), 0) AS "DiscountAmount",
                    COALESCE(SUM("MaterialCostAmount" + "LaborCostAmount"), 0) AS "CostAmount",
                    COALESCE(SUM("ProfitAmount"), 0) AS "ProfitAmount"
                FROM "MaintenanceFinancialTotals"
                WHERE "OrganizationId" = {organizationId} AND "IsVisible" AND "CurrencyCode" <> {currencyCode}
                    AND "EffectiveDate" >= {fromUtc} AND "EffectiveDate" < {toUtc}
                GROUP BY "CurrencyCode"
            )
            SELECT jsonb_build_object(
                'Kpis', (SELECT to_jsonb(totals) FROM (
                    SELECT "OrdersCount", "GrossAmount", "NetAmount", "PaidAmount", "DebtAmount", "DiscountAmount",
                        "MaterialCostAmount", "LaborCostAmount", "CostAmount", "ProfitAmount"
                    FROM financial_groups WHERE "GroupKind" = 1
                ) AS totals),
                'OtherCurrencyTotals', COALESCE((SELECT jsonb_agg(to_jsonb(other_currency_totals)) FROM other_currency_totals), '[]'::jsonb),
                'Days', COALESCE((SELECT jsonb_agg(to_jsonb(day)) FROM (
                    SELECT TIMESTAMP '0001-01-01' + "UtcDay" * INTERVAL '1 day' AS "Date", "PaidAmount", "NetAmount"
                    FROM financial_groups WHERE "GroupKind" = 0
                ) AS day), '[]'::jsonb),
                'DebtorGroups', COALESCE((SELECT jsonb_agg(to_jsonb(debtor_groups)) FROM debtor_groups), '[]'::jsonb),
                'ServiceGroups', COALESCE((SELECT jsonb_agg(to_jsonb(service_groups)) FROM service_groups), '[]'::jsonb),
                'Orders', COALESCE((SELECT jsonb_agg(to_jsonb(orders) ORDER BY orders."Date" DESC, orders."Id" DESC) FROM (
                    SELECT selected."MaintenanceId" AS "Id", selected."CurrencyCode", maintenance."VehicleId", maintenance."Status" = 4 AS "IsCancelled",
                        TIMESTAMP '0001-01-01' + selected."UtcDay" * INTERVAL '1 day' AS "Date",
                        selected."ClientId", selected."ClientName",
                        selected."GrossAmount", selected."NetAmount", selected."PaidAmount", selected."DebtAmount",
                        selected."DiscountAmount", selected."MaterialCostAmount", selected."LaborCostAmount", selected."ProfitAmount",
                        EXISTS (SELECT 1 FROM "MaintenanceDetails" detail
                            WHERE detail."MaintenanceId" = maintenance."Id" AND detail."Price" IS NOT NULL) AS "HasPricedDetail",
                        NOT EXISTS (SELECT 1 FROM "MaintenanceDetails" detail
                            WHERE detail."MaintenanceId" = maintenance."Id" AND detail."Price" IS NULL) AS "AreAllDetailsPriced",
                        CASE WHEN vehicle."BrandId" IS NOT NULL THEN COALESCE(brand."Name", '')
                            ELSE COALESCE(vehicle."ManualBrandName", '') END AS "BrandName",
                        CASE WHEN vehicle."ModelId" IS NOT NULL THEN COALESCE(model."Name", '')
                            ELSE COALESCE(vehicle."ManualModelName", '') END AS "ModelName",
                        vehicle."Name" AS "VehicleName", vehicle."YearIssue", vehicle."RegistrationPlate",
                        client."Name" AS "VehicleClientName",
                        vehicle."BrandId" IS NOT NULL OR vehicle."ModelId" IS NOT NULL AS "HasCatalogIdentity"
                    FROM (
                        SELECT * FROM "MaintenanceFinancialTotals"
                        WHERE "OrganizationId" = {organizationId} AND "IsVisible" AND "CurrencyCode" = {currencyCode}
                            AND "UtcDay" >= {firstUtcDay} AND "UtcDay" <= {lastUtcDay}
                            AND "EffectiveDate" >= {fromUtc} AND "EffectiveDate" < {toUtc}
                        ORDER BY "UtcDay" DESC, "MaintenanceId" DESC
                        LIMIT 80
                    ) AS selected
                    JOIN "Maintenance" AS maintenance ON maintenance."Id" = selected."MaintenanceId"
                    JOIN "Vehicles" AS vehicle ON vehicle."Id" = maintenance."VehicleId"
                    LEFT JOIN "Brands" AS brand ON brand."Id" = vehicle."BrandId"
                    LEFT JOIN "Models" AS model ON model."Id" = vehicle."ModelId"
                    LEFT JOIN "OrganizationClients" AS client ON client."Id" = vehicle."OrganizationClientId"
                ) AS orders), '[]'::jsonb)
            )::text AS "Value"
            """).SingleAsync();

        var aggregates = JsonSerializer.Deserialize<FinanceAggregates>(json)
            ?? throw new InvalidOperationException("Не удалось прочитать финансовые агрегаты.");
        var totals = aggregates.Kpis;
        var kpis = new OrganizationFinanceKpisDto
        {
            OrdersCount = totals.OrdersCount,
            GrossAmount = totals.GrossAmount,
            NetAmount = totals.NetAmount,
            PaidAmount = totals.PaidAmount,
            DebtAmount = totals.DebtAmount,
            DiscountAmount = totals.DiscountAmount,
            MaterialCostAmount = totals.MaterialCostAmount,
            LaborCostAmount = totals.LaborCostAmount,
            CostAmount = totals.CostAmount,
            ProfitAmount = totals.ProfitAmount,
            AverageCheckAmount = totals.OrdersCount == 0
                ? 0m
                : OrganizationFinanceMapper.RoundMoney(totals.NetAmount / totals.OrdersCount)
        };

        var dates = OrganizationFinanceMapper.EachDay(from, to).ToList();
        var days = aggregates.Days.ToDictionary(day => day.Date);

        return new OrganizationFinanceSummaryDto
        {
            CurrencyCode = currencyCode,
            OtherCurrencyTotals = aggregates.OtherCurrencyTotals,
            DateFrom = from,
            DateTo = to,
            Kpis = kpis,
            RevenueSeries = dates.Select(date => new OrganizationFinanceMoneySeriesPointDto
            {
                Date = date,
                Label = date.ToString("dd.MM"),
                Value = days.TryGetValue(date, out var day) ? day.PaidAmount : 0m
            }).ToList(),
            AccruedSeries = dates.Select(date => new OrganizationFinanceMoneySeriesPointDto
            {
                Date = date,
                Label = date.ToString("dd.MM"),
                Value = days.TryGetValue(date, out var day) ? day.NetAmount : 0m
            }).ToList(),
            CostBreakdown = OrganizationFinanceMapper.BuildMoneyBreakdown([
                ("materials", "Материалы", kpis.MaterialCostAmount),
                ("labor", "Труд", kpis.LaborCostAmount),
                ("discount", "Скидки", kpis.DiscountAmount)
            ]),
            PaymentBreakdown = OrganizationFinanceMapper.BuildMoneyBreakdown([
                ("paid", "Оплачено", kpis.PaidAmount),
                ("debt", "Долг", kpis.DebtAmount)
            ]),
            // OrdinalIgnoreCase и Unicode Trim сохраняются в .NET после точной группировки исходных имён в БД.
            TopServices = OrganizationFinanceQueryService.OrderByFirstOccurrence(aggregates.ServiceGroups)
                .Where(group => !string.IsNullOrWhiteSpace(group.Name))
                .GroupBy(group => group.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new OrganizationFinanceServiceItemDto
                {
                    Name = group.Key,
                    Count = group.Sum(item => item.Count),
                    Amount = group.Sum(item => item.Amount)
                })
                .OrderByDescending(item => item.Amount)
                .ThenBy(item => item.Name)
                .Take(8)
                .ToList(),
            TopDebtors = OrganizationFinanceQueryService.OrderByFirstOccurrence(aggregates.DebtorGroups)
                .GroupBy(group => new
                {
                    group.ClientId,
                    Name = string.IsNullOrWhiteSpace(group.Name) ? "Клиент не указан" : group.Name.Trim()
                })
                .Select(group => new OrganizationFinanceClientDebtItemDto
                {
                    ClientId = group.Key.ClientId,
                    ClientName = group.Key.Name,
                    OrdersCount = group.Sum(item => item.Count),
                    DebtAmount = group.Sum(item => item.Amount)
                })
                .OrderByDescending(item => item.DebtAmount)
                .ThenBy(item => item.ClientName)
                .Take(8)
                .ToList(),
            Orders = aggregates.Orders.Select(item =>
            {
                var paymentStatus = OrganizationFinanceMapper.ResolvePaymentStatus(
                    item.PaidAmount, item.NetAmount, item.IsCancelled || item.HasPricedDetail,
                    item.IsCancelled || item.AreAllDetailsPriced);

                return new OrganizationFinanceOrderItemDto
                {
                    Id = item.Id,
                    CurrencyCode = item.CurrencyCode,
                    Date = DateTime.SpecifyKind(item.Date, DateTimeKind.Utc),
                    VehicleId = item.VehicleId,
                    VehicleDisplayName = VehicleDisplayName.Format(item.BrandName, item.ModelName, item.RegistrationPlate),
                    VehicleSubtitle = OrganizationWorkspaceMaintenanceMapper.BuildVehicleSubtitle(
                        item.VehicleName, item.YearIssue, item.VehicleClientName, item.RegistrationPlate, !item.HasCatalogIdentity),
                    ClientId = item.ClientId,
                    ClientName = item.ClientName,
                    GrossAmount = item.GrossAmount,
                    NetAmount = item.NetAmount,
                    PaidAmount = item.PaidAmount,
                    DebtAmount = item.DebtAmount,
                    DiscountAmount = item.DiscountAmount,
                    MaterialCostAmount = item.MaterialCostAmount,
                    LaborCostAmount = item.LaborCostAmount,
                    ProfitAmount = item.ProfitAmount,
                    IsCancelled = item.IsCancelled,
                    PaymentStatus = paymentStatus,
                    PaymentStatusName = OrganizationFinanceMapper.ResolvePaymentStatusName(paymentStatus),
                    DetailUrl = $"#maintenance/{item.Id}"
                };
            }).ToList()
        };
    }

    /// <summary> Восстанавливает порядок первого появления группы в исходной ленте заказов и строк. </summary>
    private static IOrderedEnumerable<FinanceGroup> OrderByFirstOccurrence(IEnumerable<FinanceGroup> groups) => groups
        .OrderBy(group => group.FirstKey, StringComparer.Ordinal);

    private sealed class FinanceAggregates
    {
        public OrganizationFinanceKpisDto Kpis { get; set; } = new();

        public List<OrganizationFinanceCurrencyTotalsDto> OtherCurrencyTotals { get; set; } = [];

        public List<FinanceDay> Days { get; set; } = [];

        public List<FinanceGroup> ServiceGroups { get; set; } = [];

        public List<FinanceGroup> DebtorGroups { get; set; } = [];

        public List<FinanceOrder> Orders { get; set; } = [];
    }

    private sealed class FinanceDay
    {
        public DateTime Date { get; set; }

        public decimal PaidAmount { get; set; }

        public decimal NetAmount { get; set; }
    }

    private sealed class FinanceGroup
    {
        public Guid? ClientId { get; set; }

        public string? Name { get; set; }

        public int Count { get; set; }

        public decimal Amount { get; set; }

        public string FirstKey { get; set; } = string.Empty;
    }

    private sealed class FinanceOrder
    {
        public Guid Id { get; set; }

        public string CurrencyCode { get; set; } = "RUB";

        public Guid VehicleId { get; set; }

        public bool IsCancelled { get; set; }

        public DateTime Date { get; set; }

        public Guid? ClientId { get; set; }

        public string? ClientName { get; set; }

        public decimal GrossAmount { get; set; }

        public decimal NetAmount { get; set; }

        public decimal PaidAmount { get; set; }

        public decimal DebtAmount { get; set; }

        public decimal DiscountAmount { get; set; }

        public decimal MaterialCostAmount { get; set; }

        public decimal LaborCostAmount { get; set; }

        public decimal ProfitAmount { get; set; }

        public bool HasPricedDetail { get; set; }

        public bool AreAllDetailsPriced { get; set; }

        public string BrandName { get; set; } = string.Empty;

        public string ModelName { get; set; } = string.Empty;

        public string? VehicleName { get; set; }

        public int? YearIssue { get; set; }

        public string? RegistrationPlate { get; set; }

        public string? VehicleClientName { get; set; }

        public bool HasCatalogIdentity { get; set; }
    }
}
