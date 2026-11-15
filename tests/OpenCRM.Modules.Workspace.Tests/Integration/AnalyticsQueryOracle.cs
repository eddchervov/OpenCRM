using OpenCRM.Modules.Workspace.Queries;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

/// <summary> Сервис аналитики рабочего пространства организации. </summary>
public sealed class AnalyticsQueryOracle(
    OpenCRMContext opencrmContext) : IOrganizationWorkspaceAnalyticsQueryService
{
    /// <summary> Возвращает обзорные показатели рабочего пространства организации. </summary>
    public async Task<OrganizationWorkspaceOverviewMetrics> GetOverviewMetricsAsync(
        Guid organizationId,
        bool hasDefectsModule,
        bool hasCountersModule)
    {
        var maintenanceQuery = opencrmContext.Maintenance
            .AsNoTracking()
            .Where(x => x.Vehicle.OrganizationId == organizationId && !x.IsDeleted);
        var defectsQuery = opencrmContext.VehicleDefects
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && !x.IsDeleted);
        var counterEntriesQuery = opencrmContext.VehicleCounterEntries
            .AsNoTracking()
            .Where(x => x.Vehicle.OrganizationId == organizationId && !x.IsDeleted);

        return new OrganizationWorkspaceOverviewMetrics
        {
            MaintenanceTotalCount = await maintenanceQuery.CountAsync(),
            MaintenancePlannedCount = await maintenanceQuery.CountAsync(x => x.Status == MaintenanceStatus.Planned),
            MaintenanceInProgressCount = await maintenanceQuery.CountAsync(x => x.Status == MaintenanceStatus.InProgress),
            MaintenanceCompletedCount = await maintenanceQuery.CountAsync(x => x.Status == MaintenanceStatus.Completed),
            OpenDefectsCount = hasDefectsModule ? await defectsQuery.CountAsync(x => x.StatusCode != VehicleDefectStatusCodes.Resolved) : 0,
            ResolvedDefectsCount = hasDefectsModule ? await defectsQuery.CountAsync(x => x.StatusCode == VehicleDefectStatusCodes.Resolved) : 0,
            CounterEntriesCount = hasCountersModule ? await counterEntriesQuery.CountAsync() : 0,
            DocumentsCount = await opencrmContext.MaintenanceDocuments
                .AsNoTracking()
                .CountAsync(x => x.OrganizationId == organizationId && !x.IsDeleted)
        };
    }

    /// <summary> Возвращает аналитику рабочего пространства организации с учётом доступных модулей. </summary>
    public async Task<OrganizationWorkspaceAnalytics> GetAnalyticsAsync(
        Guid organizationId,
        bool hasClientsModule,
        bool hasDefectsModule,
        bool hasCountersModule,
        bool hasMaterialsModule,
        DateTime today)
    {
        var from = today.Date.AddDays(-13);
        var toExclusive = today.Date.AddDays(1);
        var dates = OrganizationFinanceMapper.EachDay(from, today.Date).ToList();

        var maintenanceQuery = opencrmContext.Maintenance
            .AsNoTracking()
            .Where(x => x.Vehicle.OrganizationId == organizationId && !x.IsDeleted && !x.Vehicle.IsDeleted);

        var activeVehicles = await opencrmContext.Vehicles
            .AsNoTracking()
            .CountAsync(x => x.OrganizationId == organizationId && !x.IsDeleted && !x.IsArchived);
        var clients = hasClientsModule
            ? await opencrmContext.OrganizationClients
                .AsNoTracking()
                .CountAsync(x => x.OrganizationId == organizationId && !x.IsDeleted)
            : 0;
        var plannedWorks = await maintenanceQuery.CountAsync(x => x.Status == MaintenanceStatus.Planned);
        var inProgressWorks = await maintenanceQuery.CountAsync(x => x.Status == MaintenanceStatus.InProgress);
        var completedWorks14Days = await maintenanceQuery.CountAsync(x =>
            x.Status == MaintenanceStatus.Completed &&
            (x.CompletedDate ?? x.Date) >= from &&
            (x.CompletedDate ?? x.Date) < toExclusive);
        var openDefects = hasDefectsModule
            ? await opencrmContext.VehicleDefects
                .AsNoTracking()
                .CountAsync(x => x.OrganizationId == organizationId && !x.IsDeleted && x.StatusCode != VehicleDefectStatusCodes.Resolved)
            : 0;
        var counterEntries14Days = hasCountersModule
            ? await opencrmContext.VehicleCounterEntries
                .AsNoTracking()
                .CountAsync(x => x.Vehicle.OrganizationId == organizationId && !x.IsDeleted && x.RecordedAt >= from && x.RecordedAt < toExclusive)
            : 0;
        var documents14Days = await opencrmContext.MaintenanceDocuments
            .AsNoTracking()
            .CountAsync(x => x.OrganizationId == organizationId && !x.IsDeleted && x.CreatedAt >= from && x.CreatedAt < toExclusive);

        var materialStocks = hasMaterialsModule
            ? await opencrmContext.OrganizationMaterialStocks
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId)
                .Select(x => new MaterialStockAnalyticsRow
                {
                    QuantityOnHand = x.QuantityOnHand,
                    MinQuantity = x.MinQuantity
                })
                .ToListAsync()
            : [];
        var materialsOutOfStock = materialStocks.Count(x => x.QuantityOnHand <= 0);
        var materialsLowStock = materialStocks.Count(x => x.QuantityOnHand > 0 && x.QuantityOnHand <= x.MinQuantity);

        var createdPoints = await maintenanceQuery
            .Where(x => x.DateTimeCreation >= from && x.DateTimeCreation < toExclusive)
            .GroupBy(x => x.DateTimeCreation.Date)
            .Select(x => new DailyPoint(x.Key, x.Count()))
            .ToDictionaryAsync(x => x.Date, x => x.Count);
        var completedPoints = await maintenanceQuery
            .Where(x => x.Status == MaintenanceStatus.Completed && (x.CompletedDate ?? x.Date) >= from && (x.CompletedDate ?? x.Date) < toExclusive)
            .GroupBy(x => (x.CompletedDate ?? x.Date).Date)
            .Select(x => new DailyPoint(x.Key, x.Count()))
            .ToDictionaryAsync(x => x.Date, x => x.Count);

        var completedRows = await maintenanceQuery
            .Where(x => x.Status == MaintenanceStatus.Completed && (x.CompletedDate ?? x.Date) >= from && (x.CompletedDate ?? x.Date) < toExclusive)
            .Select(x => new
            {
                Date = (x.CompletedDate ?? x.Date).Date,
                x.PaidAmount,
                x.DiscountAmount,
                Details = x.Details.OrderBy(detail => detail.Position).ToList()
            })
            .ToListAsync();
        var revenuePoints = completedRows
            .GroupBy(x => x.Date)
            .ToDictionary(
                x => x.Key,
                x => x.Sum(item => AnalyticsQueryOracle.ResolveMaintenanceAmount(item.PaidAmount, item.DiscountAmount, item.Details)));

        var statusCounts = new[]
        {
            new { Code = "planned", Label = "Запланировано", Value = plannedWorks },
            new { Code = "inProgress", Label = "В работе", Value = inProgressWorks },
            new { Code = "completed14", Label = "Завершено за 14 дней", Value = completedWorks14Days }
        };
        var statusTotal = Math.Max(1, statusCounts.Sum(x => x.Value));

        return new OrganizationWorkspaceAnalytics
        {
            Kpis = new OrganizationWorkspaceAnalyticsKpis
            {
                ActiveVehicles = activeVehicles,
                Clients = clients,
                PlannedWorks = plannedWorks,
                InProgressWorks = inProgressWorks,
                CompletedWorks14Days = completedWorks14Days,
                Revenue14Days = revenuePoints.Values.Sum(),
                OpenDefects = openDefects,
                CounterEntries14Days = counterEntries14Days,
                MaterialsLowStock = materialsLowStock,
                MaterialsOutOfStock = materialsOutOfStock,
                Documents14Days = documents14Days
            },
            MaintenanceCreatedSeries = dates.Select(date => new OrganizationWorkspaceAnalyticsSeriesPoint
            {
                Date = date,
                Label = date.ToString("dd.MM"),
                Value = createdPoints.TryGetValue(date, out var value) ? value : 0
            }).ToList(),
            MaintenanceCompletedSeries = dates.Select(date => new OrganizationWorkspaceAnalyticsSeriesPoint
            {
                Date = date,
                Label = date.ToString("dd.MM"),
                Value = completedPoints.TryGetValue(date, out var value) ? value : 0
            }).ToList(),
            RevenueSeries = dates.Select(date => new OrganizationWorkspaceAnalyticsMoneySeriesPoint
            {
                Date = date,
                Label = date.ToString("dd.MM"),
                Value = revenuePoints.TryGetValue(date, out var value) ? value : 0m
            }).ToList(),
            StatusBreakdown = statusCounts.Select(x => new OrganizationWorkspaceAnalyticsBreakdownItem
            {
                Code = x.Code,
                Label = x.Label,
                Value = x.Value,
                Percent = (int)Math.Round(x.Value * 100m / statusTotal)
            }).ToList(),
            MaterialRiskBreakdown =
            [
                new OrganizationWorkspaceAnalyticsBreakdownItem
                {
                    Code = "out",
                    Label = "Нет в наличии",
                    Value = materialsOutOfStock,
                    Percent = materialStocks.Count == 0 ? 0 : (int)Math.Round(materialsOutOfStock * 100m / materialStocks.Count)
                },
                new OrganizationWorkspaceAnalyticsBreakdownItem
                {
                    Code = "low",
                    Label = "Ниже минимума",
                    Value = materialsLowStock,
                    Percent = materialStocks.Count == 0 ? 0 : (int)Math.Round(materialsLowStock * 100m / materialStocks.Count)
                }
            ]
        };
    }

    /// <summary> Определяет сумму обслуживания по оплате, скидке и строкам работ. </summary>
    private static decimal ResolveMaintenanceAmount(
        decimal paidAmount,
        decimal discountAmount,
        IReadOnlyList<MaintenanceDetail> details)
    {
        if (paidAmount > 0)
        {
            return paidAmount;
        }

        var detailsTotal = details.Sum(x => (decimal)(x.Price ?? 0));

        return Math.Max(0m, detailsTotal - discountAmount);
    }

    /// <summary> Точка дневного счётчика для аналитики. </summary>
    private sealed record DailyPoint(DateTime Date, int Count);

    /// <summary> Строка аналитики запасов материалов. </summary>
    private sealed class MaterialStockAnalyticsRow
    {
        /// <summary> Доступное количество материала. </summary>
        public decimal QuantityOnHand { get; init; }

        /// <summary> Минимальное количество материала. </summary>
        public decimal MinQuantity { get; init; }
    }
}
