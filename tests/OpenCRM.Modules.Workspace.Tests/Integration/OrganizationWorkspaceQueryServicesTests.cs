using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Workspace.Queries;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Testing;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class OrganizationWorkspaceQueryServicesTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Fact]
    public async Task FinanceSummaryNormalizesDatesAndCalculatesOrganizationScopedTotals()
    {
        await using var context = fixture.CreateContext();
        var (user, organization) = CreateOrganization("Finance query coverage");
        var client = new OrganizationClient { Organization = organization, Name = "Клиент из карточки" };
        var vehicle = CreateVehicle(user, organization, client);
        var serviceDate = UtcDate(2026, 10, 10);
        var completed = CreateMaintenance(vehicle, serviceDate);
        completed.Status = MaintenanceStatus.Completed;
        completed.CompletedDate = serviceDate;
        completed.DiscountAmount = 100m;
        completed.PaidAmount = 500m;
        completed.MaterialCostAmount = 100m;
        completed.LaborCostAmount = 200m;
        completed.BuyerClientId = client.Id;
        completed.BuyerName = "  Покупатель по заказу  ";
        completed.Details =
        [
            new() { Name = " Полировка ", Price = 1000m },
            new() { Name = "полировка", Price = 500m },
            new() { Name = "Диагностика", Price = 0m },
            new() { Name = "", Price = 50m }
        ];
        var unpaid = CreateMaintenance(vehicle, serviceDate.AddDays(-1));
        unpaid.PlannedDate = serviceDate.AddDays(-1);
        unpaid.CompletedDate = null;
        unpaid.PaidAmount = 0m;
        unpaid.DiscountAmount = 300m;
        unpaid.BuyerClientId = client.Id;
        unpaid.BuyerName = client.Name;
        unpaid.Details = [new() { Name = "Регулировка", Price = 200m }];
        var blankBuyerVehicle = CreateVehicle(user, organization, client);
        var blankBuyerOrder = CreateMaintenance(blankBuyerVehicle, serviceDate);
        blankBuyerOrder.BuyerName = "Снимок покупателя без связи";
        blankBuyerOrder.Details = [new() { Name = "Замена", Price = 100m }];
        var dateFallbackOrder = CreateMaintenance(vehicle, serviceDate);
        dateFallbackOrder.PlannedDate = null;
        dateFallbackOrder.CompletedDate = null;
        dateFallbackOrder.Details = [new() { Name = "Снимок даты", Price = 0m }];
        var catalogBrand = new Brand { Name = "Volvo" };
        var catalogModel = new Model { Name = "XC90", Brand = catalogBrand };
        var catalogVehicle = CreateVehicle(user, organization);
        catalogVehicle.Model = catalogModel;
        catalogVehicle.ManualBrandName = "Volvo";
        catalogVehicle.ManualModelName = null;
        var catalogOrder = CreateMaintenance(catalogVehicle, serviceDate);
        catalogOrder.Details = [];
        var brandOnly = new Brand { Name = "Toyota" };
        var brandOnlyVehicle = CreateVehicle(user, organization);
        brandOnlyVehicle.Brand = brandOnly;
        brandOnlyVehicle.ManualBrandName = null;
        var brandOnlyOrder = CreateMaintenance(brandOnlyVehicle, serviceDate);
        brandOnlyOrder.Details = [new() { Name = "Брендовая услуга", Price = 0m }];
        var outsideRange = CreateMaintenance(vehicle, serviceDate.AddDays(1));
        outsideRange.CompletedDate = serviceDate.AddDays(1);
        var deleted = CreateMaintenance(vehicle, serviceDate);
        deleted.IsDeleted = true;
        var deletedVehicle = CreateVehicle(user, organization);
        deletedVehicle.IsDeleted = true;
        var deletedVehicleMaintenance = CreateMaintenance(deletedVehicle, serviceDate);
        var (foreignUser, foreignOrganization) = CreateOrganization("Foreign finance query coverage");
        var foreignVehicle = CreateVehicle(foreignUser, foreignOrganization);
        var foreignMaintenance = CreateMaintenance(foreignVehicle, serviceDate);

        context.Users.AddRange(user, foreignUser);
        context.Organizations.AddRange(organization, foreignOrganization);
        context.OrganizationClients.Add(client);
        context.Brands.Add(catalogBrand);
        context.Brands.Add(brandOnly);
        context.Models.Add(catalogModel);
        context.Vehicles.AddRange(catalogVehicle, brandOnlyVehicle);
        context.Maintenance.AddRange(
            completed,
            unpaid,
            blankBuyerOrder,
            dateFallbackOrder,
            catalogOrder,
            brandOnlyOrder,
            outsideRange,
            deleted,
            deletedVehicleMaintenance,
            foreignMaintenance);
        await context.SaveChangesAsync();

        var service = new OrganizationFinanceQueryService(context);
        var summary = await service.GetFinanceSummaryAsync(organization.Id, serviceDate, serviceDate.AddDays(-1));

        Assert.Equal(serviceDate.AddDays(-1), summary.DateFrom);
        Assert.Equal(serviceDate, summary.DateTo);
        Assert.Equal(6, summary.Kpis.OrdersCount);
        Assert.Equal(1850m, summary.Kpis.GrossAmount);
        Assert.Equal(1550m, summary.Kpis.NetAmount);
        Assert.Equal(500m, summary.Kpis.PaidAmount);
        Assert.Equal(1050m, summary.Kpis.DebtAmount);
        Assert.Equal(400m, summary.Kpis.DiscountAmount);
        Assert.Equal(100m, summary.Kpis.MaterialCostAmount);
        Assert.Equal(200m, summary.Kpis.LaborCostAmount);
        Assert.Equal(300m, summary.Kpis.CostAmount);
        Assert.Equal(1250m, summary.Kpis.ProfitAmount);
        Assert.Equal(258.33m, summary.Kpis.AverageCheckAmount);
        Assert.Equal(new[] { 0m, 500m }, summary.RevenueSeries.Select(point => point.Value));
        Assert.Equal(new[] { 0m, 1550m }, summary.AccruedSeries.Select(point => point.Value));
        var serviceItem = Assert.Single(summary.TopServices, item => item.Name == "Полировка");
        Assert.Equal("Полировка", serviceItem.Name, ignoreCase: true);
        Assert.Equal(2, serviceItem.Count);
        Assert.Equal(1500m, serviceItem.Amount);
        var selectedBuyerDebtor = Assert.Single(summary.TopDebtors, item => item.ClientName == "Покупатель по заказу");
        Assert.Equal(1, selectedBuyerDebtor.OrdersCount);
        Assert.Equal(950m, selectedBuyerDebtor.DebtAmount);
        var unidentifiedDebtor = Assert.Single(summary.TopDebtors, item => item.ClientName == "Клиент не указан");
        Assert.Equal(1, unidentifiedDebtor.OrdersCount);
        Assert.Equal(100m, unidentifiedDebtor.DebtAmount);
        var completedOrder = Assert.Single(summary.Orders, order => order.PaymentStatus == "partial");
        Assert.Equal("Частично", completedOrder.PaymentStatusName);
        Assert.Equal("  Покупатель по заказу  ", completedOrder.ClientName);
        Assert.EndsWith(completedOrder.Id.ToString(), completedOrder.DetailUrl);
        var unpaidOrder = Assert.Single(summary.Orders, order => order.Id == unpaid.Id);
        Assert.Equal(MaintenancePaymentStatus.NoPaymentRequired, unpaidOrder.PaymentStatus);
        Assert.Equal(client.Id, unpaidOrder.ClientId);
        Assert.Equal("Клиент из карточки", unpaidOrder.ClientName);
        var blankBuyerSummaryOrder = Assert.Single(summary.Orders, order => order.Id == blankBuyerOrder.Id);
        Assert.Null(blankBuyerSummaryOrder.ClientId);
        Assert.Null(blankBuyerSummaryOrder.ClientName);
        var dateFallbackSummaryOrder = Assert.Single(summary.Orders, order => order.Id == dateFallbackOrder.Id);
        Assert.Equal(serviceDate, dateFallbackSummaryOrder.Date);
        Assert.Equal(MaintenancePaymentStatus.NoPaymentRequired, dateFallbackSummaryOrder.PaymentStatus);
        var emptyOrder = Assert.Single(summary.Orders, order => order.Id == catalogOrder.Id);
        Assert.Equal("unpaid", emptyOrder.PaymentStatus);
        var catalogVehicleOrder = Assert.Single(summary.Orders, order => order.VehicleId == catalogVehicle.Id);
        Assert.Contains("XC90", catalogVehicleOrder.VehicleDisplayName);
        var brandOnlyVehicleOrder = Assert.Single(summary.Orders, order => order.VehicleId == brandOnlyVehicle.Id);
        Assert.Contains("Toyota", brandOnlyVehicleOrder.VehicleDisplayName);
        Assert.Equal(57, summary.CostBreakdown.Single(item => item.Code == "discount").Percent);
        Assert.Equal(32, summary.PaymentBreakdown.Single(item => item.Code == "paid").Percent);

        var empty = await service.GetFinanceSummaryAsync(organization.Id, serviceDate.AddDays(3), serviceDate.AddDays(4));

        Assert.Equal(0, empty.Kpis.OrdersCount);
        Assert.Equal(0m, empty.Kpis.AverageCheckAmount);
        Assert.Empty(empty.TopServices);
        Assert.Empty(empty.TopDebtors);
        Assert.Empty(empty.CostBreakdown);
        Assert.Empty(empty.PaymentBreakdown);
    }

    [Fact]
    public async Task AnalyticsCombinesModuleFlagsTimeWindowsAndStockRisk()
    {
        await using var context = fixture.CreateContext();
        var (user, organization) = CreateOrganization("Analytics query coverage");
        var vehicle = CreateVehicle(user, organization);
        var archivedVehicle = CreateVehicle(user, organization);
        archivedVehicle.IsArchived = true;
        var deletedVehicle = CreateVehicle(user, organization);
        deletedVehicle.IsDeleted = true;
        var today = UtcDate(2026, 10, 13);
        var planned = CreateMaintenance(vehicle, today);
        var inProgress = CreateMaintenance(vehicle, today.AddDays(-1));
        inProgress.Status = MaintenanceStatus.InProgress;
        var completedPaid = CreateMaintenance(vehicle, today.AddDays(-2));
        completedPaid.Status = MaintenanceStatus.Completed;
        completedPaid.CompletedDate = today.AddDays(-2);
        completedPaid.DateTimeCreation = today.AddDays(-2);
        completedPaid.PaidAmount = 75m;
        completedPaid.Details = [new() { Name = "Услуга", Price = 100m }];
        var completedFallback = CreateMaintenance(vehicle, today.AddDays(-3));
        completedFallback.Status = MaintenanceStatus.Completed;
        completedFallback.CompletedDate = null;
        completedFallback.Date = today.AddDays(-3);
        completedFallback.DateTimeCreation = today.AddDays(-3);
        completedFallback.DiscountAmount = 150m;
        completedFallback.Details = [new() { Name = "Скидка больше цены", Price = 100m }];
        var completedEmptyDetails = CreateMaintenance(vehicle, today.AddDays(-4));
        completedEmptyDetails.Status = MaintenanceStatus.Completed;
        completedEmptyDetails.CompletedDate = null;
        completedEmptyDetails.DateTimeCreation = today.AddDays(-4);
        completedEmptyDetails.Details = [];
        var completedOutside = CreateMaintenance(vehicle, today.AddDays(-14));
        completedOutside.Status = MaintenanceStatus.Completed;
        completedOutside.CompletedDate = today.AddDays(-14);
        var softDeleted = CreateMaintenance(vehicle, today);
        softDeleted.IsDeleted = true;
        var client = new OrganizationClient { Organization = organization, Name = "Активный клиент" };
        var deletedClient = new OrganizationClient { Organization = organization, Name = "Удалённый клиент", IsDeleted = true };
        var openDefect = CreateDefect(user, organization, vehicle);
        var resolvedDefect = CreateDefect(user, organization, vehicle);
        resolvedDefect.StatusCode = VehicleDefectStatusCodes.Resolved;
        var deletedDefect = CreateDefect(user, organization, vehicle);
        deletedDefect.IsDeleted = true;
        var counterInWindow = new VehicleCounterEntry { Vehicle = vehicle, User = user, Value = 100, RecordedAt = today.AddDays(-13) };
        var counterOutside = new VehicleCounterEntry { Vehicle = vehicle, User = user, Value = 101, RecordedAt = today.AddDays(-14) };
        var documentInWindow = CreateDocument(user, organization, completedPaid, today);
        var documentOutside = CreateDocument(user, organization, completedPaid, today.AddDays(-14));
        documentOutside.Format = "docx";
        var deletedDocument = CreateDocument(user, organization, completedPaid, today);
        deletedDocument.IsDeleted = true;
        deletedDocument.Format = "html";
        var stocks = new[]
        {
            CreateStock(organization, "Нет", 0m, 2m),
            CreateStock(organization, "Мало", 2m, 2m),
            CreateStock(organization, "Достаточно", 5m, 2m)
        };

        context.Users.Add(user);
        context.Organizations.Add(organization);
        context.OrganizationClients.AddRange(client, deletedClient);
        context.Maintenance.AddRange(
            planned,
            inProgress,
            completedPaid,
            completedFallback,
            completedEmptyDetails,
            completedOutside,
            softDeleted);
        context.Maintenance.Add(CreateMaintenance(deletedVehicle, today));
        context.VehicleDefects.AddRange(openDefect, resolvedDefect, deletedDefect);
        context.VehicleCounterEntries.AddRange(counterInWindow, counterOutside);
        context.MaintenanceDocuments.AddRange(documentInWindow, documentOutside, deletedDocument);
        context.OrganizationMaterialStocks.AddRange(stocks);
        await context.SaveChangesAsync();

        var service = new OrganizationWorkspaceAnalyticsQueryService(context);
        var metrics = await service.GetOverviewMetricsAsync(organization.Id, hasDefectsModule: true, hasCountersModule: true);
        Assert.Equal(7, metrics.MaintenanceTotalCount);
        Assert.Equal(2, metrics.MaintenancePlannedCount);
        Assert.Equal(1, metrics.MaintenanceInProgressCount);
        Assert.Equal(4, metrics.MaintenanceCompletedCount);
        Assert.Equal(1, metrics.OpenDefectsCount);
        Assert.Equal(1, metrics.ResolvedDefectsCount);
        Assert.Equal(2, metrics.CounterEntriesCount);
        Assert.Equal(2, metrics.DocumentsCount);

        var analytics = await service.GetAnalyticsAsync(
            organization.Id,
            hasClientsModule: true,
            hasDefectsModule: true,
            hasCountersModule: true,
            hasMaterialsModule: true,
            today);

        Assert.Equal(1, analytics.Kpis.ActiveVehicles);
        Assert.Equal(1, analytics.Kpis.Clients);
        Assert.Equal(1, analytics.Kpis.PlannedWorks);
        Assert.Equal(1, analytics.Kpis.InProgressWorks);
        Assert.Equal(3, analytics.Kpis.CompletedWorks14Days);
        Assert.Equal(75m, analytics.Kpis.Revenue14Days);
        Assert.Equal(1, analytics.Kpis.OpenDefects);
        Assert.Equal(1, analytics.Kpis.CounterEntries14Days);
        Assert.Equal(1, analytics.Kpis.MaterialsLowStock);
        Assert.Equal(1, analytics.Kpis.MaterialsOutOfStock);
        Assert.Equal(1, analytics.Kpis.Documents14Days);
        Assert.Equal(14, analytics.MaintenanceCreatedSeries.Count);
        Assert.Equal(14, analytics.MaintenanceCompletedSeries.Count);
        Assert.Equal(14, analytics.RevenueSeries.Count);
        Assert.Equal(1, analytics.MaintenanceCompletedSeries.Single(point => point.Date == today.AddDays(-2)).Value);
        Assert.Equal(0, analytics.MaintenanceCreatedSeries.Single(point => point.Date == today.AddDays(-13)).Value);
        Assert.Collection(analytics.StatusBreakdown,
            plannedStatus => Assert.Equal(20, plannedStatus.Percent),
            inProgressStatus => Assert.Equal(20, inProgressStatus.Percent),
            completedStatus => Assert.Equal(60, completedStatus.Percent));
        Assert.Equal(33, analytics.MaterialRiskBreakdown[0].Percent);
        Assert.Equal(33, analytics.MaterialRiskBreakdown[1].Percent);

        var disabledModules = await service.GetAnalyticsAsync(
            organization.Id,
            hasClientsModule: false,
            hasDefectsModule: false,
            hasCountersModule: false,
            hasMaterialsModule: false,
            today);

        Assert.Equal(0, disabledModules.Kpis.Clients);
        Assert.Equal(0, disabledModules.Kpis.OpenDefects);
        Assert.Equal(0, disabledModules.Kpis.CounterEntries14Days);
        Assert.Equal(0, disabledModules.Kpis.MaterialsLowStock);
        Assert.Equal(0, disabledModules.Kpis.MaterialsOutOfStock);
        Assert.All(disabledModules.MaterialRiskBreakdown, item => Assert.Equal(0, item.Percent));
        var hiddenMetrics = await service.GetOverviewMetricsAsync(organization.Id, hasDefectsModule: false, hasCountersModule: false);
        Assert.Equal(0, hiddenMetrics.OpenDefectsCount);
        Assert.Equal(0, hiddenMetrics.ResolvedDefectsCount);
        Assert.Equal(0, hiddenMetrics.CounterEntriesCount);

        var (emptyUser, emptyOrganization) = CreateOrganization("Empty analytics query coverage");
        context.Users.Add(emptyUser);
        context.Organizations.Add(emptyOrganization);
        await context.SaveChangesAsync();
        var emptyAnalytics = await service.GetAnalyticsAsync(
            emptyOrganization.Id,
            hasClientsModule: true,
            hasDefectsModule: true,
            hasCountersModule: true,
            hasMaterialsModule: true,
            today);
        Assert.Equal(0, emptyAnalytics.Kpis.ActiveVehicles);
        Assert.Equal(0, emptyAnalytics.Kpis.PlannedWorks);
        Assert.Equal(0, emptyAnalytics.Kpis.InProgressWorks);
        Assert.Equal(0, emptyAnalytics.Kpis.CompletedWorks14Days);
        Assert.Equal(0m, emptyAnalytics.Kpis.Revenue14Days);
        Assert.All(emptyAnalytics.StatusBreakdown, item => Assert.Equal(0, item.Percent));
        Assert.All(emptyAnalytics.MaterialRiskBreakdown, item => Assert.Equal(0, item.Percent));
        Assert.All(emptyAnalytics.MaintenanceCreatedSeries, point => Assert.Equal(0, point.Value));
        Assert.All(emptyAnalytics.MaintenanceCompletedSeries, point => Assert.Equal(0, point.Value));
        Assert.All(emptyAnalytics.RevenueSeries, point => Assert.Equal(0m, point.Value));
    }

    [Fact]
    public async Task PlanningUsesOrganizationRulesAndSeparatesDateMileageDefectFirstServiceAndCounterAttention()
    {
        await using var context = fixture.CreateContext();
        var (user, organization) = CreateOrganization("Planning query coverage");
        organization.PrimaryScenarioCode = OrganizationTypeCodes.Fleet;
        var dateOverdueVehicle = CreateVehicle(user, organization);
        var dateSoonVehicle = CreateVehicle(user, organization);
        var mileageSoonVehicle = CreateVehicle(user, organization);
        mileageSoonVehicle.CurrentMileage = 15_000;
        mileageSoonVehicle.MileageUnit = VehicleMileageUnit.Kilometers;
        var mileageOverdueVehicle = CreateVehicle(user, organization);
        mileageOverdueVehicle.CurrentMileage = 16_000;
        mileageOverdueVehicle.MileageUnit = VehicleMileageUnit.Kilometers;
        var noMileageVehicle = CreateVehicle(user, organization);
        var firstServiceVehicle = CreateVehicle(user, organization);
        firstServiceVehicle.DateTimeCreation = UtcDate(2026, 10, 8);
        var today = UtcDate(2026, 10, 13);
        var staleCounterVehicle = CreateVehicle(user, organization);
        staleCounterVehicle.CurrentMileage = 10;
        staleCounterVehicle.DateTimeCreation = today;
        staleCounterVehicle.CurrentMileageUpdatedAt = today.AddDays(-5);
        var dateOverdue = CreateMaintenance(dateOverdueVehicle, today.AddDays(-45));
        dateOverdue.Mileage = null;
        var dateSoon = CreateMaintenance(dateSoonVehicle, today.AddDays(-40));
        dateSoon.Mileage = null;
        var mileageSoon = CreateMaintenance(mileageSoonVehicle, today.AddDays(-10));
        mileageSoon.Mileage = 10_000;
        var mileageOverdue = CreateMaintenance(mileageOverdueVehicle, today.AddDays(-10));
        mileageOverdue.Mileage = 10_000;
        var noMileage = CreateMaintenance(noMileageVehicle, today.AddDays(-10));
        noMileage.Mileage = null;
        var rules = new[]
        {
            CreateRule(organization, "date", "Дата обслуживания", "45 дней", sortOrder: 2),
            CreateRule(organization, "mileage", "Пробег", "6000 км, 100 моточасов", sortOrder: 3),
            CreateRule(organization, "defect", "Дефекты", null, sortOrder: 4),
            CreateRule(organization, "counter", "Счётчик", "5 дней", sortOrder: 1),
            CreateRule(organization, "first", "Первый осмотр", "4 дня", sortOrder: 5),
            CreateRule(organization, "ignored", "Неактивное правило", "1 день", isActive: false, sortOrder: 6)
        };
        var openDefect = CreateDefect(user, organization, dateSoonVehicle);
        var resolvedDefect = CreateDefect(user, organization, dateSoonVehicle);
        resolvedDefect.StatusCode = VehicleDefectStatusCodes.Resolved;
        var deletedDefect = CreateDefect(user, organization, dateSoonVehicle);
        deletedDefect.IsDeleted = true;

        context.Users.Add(user);
        context.Organizations.Add(organization);
        context.Vehicles.AddRange(
            dateOverdueVehicle,
            dateSoonVehicle,
            mileageSoonVehicle,
            mileageOverdueVehicle,
            noMileageVehicle,
            firstServiceVehicle,
            staleCounterVehicle);
        context.OrganizationScenarioCatalogItems.AddRange(rules);
        context.Maintenance.AddRange(dateOverdue, dateSoon, mileageSoon, mileageOverdue, noMileage);
        context.VehicleDefects.AddRange(openDefect, resolvedDefect, deletedDefect);
        await context.SaveChangesAsync();

        var savedRules = await context.OrganizationScenarioCatalogItems
            .Where(item => item.OrganizationId == organization.Id && item.KindCode == OrganizationScenarioCatalogKindCodes.PlanningRule)
            .Select(item => new OrganizationPlanningRuleSnapshot(item.Code, item.Name, item.Description, item.IsActive))
            .ToListAsync();
        var policy = OrganizationPlanningPolicyFactory.Build(
            OrganizationScenarioProcessCatalog.Resolve(organization.PrimaryScenarioCode),
            savedRules);
        Assert.True(policy.EnableFirstService);
        Assert.True(policy.EnableCounterFreshness);
        Assert.Equal(4, policy.FirstServiceGraceDays);
        Assert.Equal(5, policy.CounterStaleWindowDays);
        var firstServiceCreatedAt = await context.Vehicles
            .Where(vehicle => vehicle.Id == firstServiceVehicle.Id)
            .Select(vehicle => vehicle.DateTimeCreation)
            .SingleAsync();
        var staleCounterUpdatedAt = await context.Vehicles
            .Where(vehicle => vehicle.Id == staleCounterVehicle.Id)
            .Select(vehicle => vehicle.CurrentMileageUpdatedAt)
            .SingleAsync();
        Assert.Equal(UtcDate(2026, 10, 8), firstServiceCreatedAt);
        Assert.Equal(UtcDate(2026, 10, 8), staleCounterUpdatedAt);

        var service = new OrganizationWorkspacePlanningQueryService(context);
        var disabled = await service.GetPlanningSummaryAsync(
            organization.Id,
            hasDefectsModule: true,
            hasCountersModule: true,
            hasSchedulerModule: false,
            today);
        AssertPlanningSummary(disabled, 0, 0, 0, 0, 0, 0);

        var summary = await service.GetPlanningSummaryAsync(
            organization.Id,
            hasDefectsModule: true,
            hasCountersModule: true,
            hasSchedulerModule: true,
            today);

        Assert.Equal(3, summary.OverdueCount);
        Assert.Equal(2, summary.SoonCount);
        Assert.True(summary.AttentionCount == 2,
            $"Attention={summary.AttentionCount}, first={summary.FirstServiceCount}, counters={summary.CounterUpdateCount}, firstCreated={firstServiceCreatedAt:O}, counterUpdated={staleCounterUpdatedAt:O}");
        Assert.Equal(1, summary.DefectsCount);
        Assert.Equal(1, summary.FirstServiceCount);
        Assert.Equal(1, summary.CounterUpdateCount);

        var withoutFeatureModules = await service.GetPlanningSummaryAsync(
            organization.Id,
            hasDefectsModule: false,
            hasCountersModule: false,
            hasSchedulerModule: true,
            today);

        Assert.Equal(2, withoutFeatureModules.OverdueCount);
        Assert.Equal(2, withoutFeatureModules.SoonCount);
        Assert.Equal(1, withoutFeatureModules.AttentionCount);
        Assert.Equal(0, withoutFeatureModules.DefectsCount);
        Assert.Equal(1, withoutFeatureModules.FirstServiceCount);
        Assert.Equal(0, withoutFeatureModules.CounterUpdateCount);

        var (emptyUser, emptyOrganization) = CreateOrganization("Empty planning query coverage");
        context.Users.Add(emptyUser);
        context.Organizations.Add(emptyOrganization);
        await context.SaveChangesAsync();
        var empty = await service.GetPlanningSummaryAsync(
            emptyOrganization.Id,
            hasDefectsModule: true,
            hasCountersModule: true,
            hasSchedulerModule: true,
            today);

        AssertPlanningSummary(empty, 0, 0, 0, 0, 0, 0);

        var (carWashUser, carWashOrganization) = CreateOrganization("Car wash planning query coverage");
        carWashOrganization.PrimaryScenarioCode = OrganizationTypeCodes.CarWash;
        var carWashVehicle = CreateVehicle(carWashUser, carWashOrganization);
        carWashVehicle.CurrentMileage = 500_000;
        carWashVehicle.CurrentMileageUpdatedAt = today.AddDays(-100);
        var carWashFirstServiceVehicle = CreateVehicle(carWashUser, carWashOrganization);
        var carWashMaintenance = CreateMaintenance(carWashVehicle, today.AddDays(-31));
        carWashMaintenance.Mileage = 0;
        context.Users.Add(carWashUser);
        context.Organizations.Add(carWashOrganization);
        context.Vehicles.Add(carWashFirstServiceVehicle);
        context.Maintenance.Add(carWashMaintenance);
        await context.SaveChangesAsync();
        var carWashSummary = await service.GetPlanningSummaryAsync(
            carWashOrganization.Id,
            hasDefectsModule: true,
            hasCountersModule: true,
            hasSchedulerModule: true,
            today);

        AssertPlanningSummary(carWashSummary, 1, 0, 0, 0, 0, 0);
    }

    private static (ApplicationUser User, Organization Organization) CreateOrganization(string name)
    {
        var user = new ApplicationUser();
        var organization = new Organization
        {
            Name = name,
            OwnerUser = user,
            PrimaryScenarioCode = OrganizationTypeCodes.Detailing
        };

        return (user, organization);
    }

    private static Vehicle CreateVehicle(ApplicationUser user, Organization organization, OrganizationClient? client = null) =>
        new()
        {
            User = user,
            Organization = organization,
            OrganizationClient = client,
            Name = "Автомобиль клиента",
            ManualBrandName = "BMW",
            ManualModelName = "X5",
            RegistrationPlate = "А123АА154",
            DateTimeCreation = UtcDate(2026, 10, 1)
        };

    private static MaintenanceEntity CreateMaintenance(Vehicle vehicle, DateTime date) =>
        new()
        {
            User = vehicle.User,
            Vehicle = vehicle,
            Date = date,
            PlannedDate = date,
            DateTimeCreation = date,
            Type = MaintenanceType.Other,
            Status = MaintenanceStatus.Planned,
            Details = [new() { Name = "Работа", Price = 100m }]
        };

    private static VehicleDefect CreateDefect(ApplicationUser user, Organization organization, Vehicle vehicle) =>
        new()
        {
            User = user,
            Organization = organization,
            Vehicle = vehicle,
            Title = "Дефект"
        };

    private static MaintenanceDocument CreateDocument(
        ApplicationUser user,
        Organization organization,
        MaintenanceEntity maintenance,
        DateTime createdAt) =>
        new()
        {
            User = user,
            CreatedByUserId = user.Id,
            Maintenance = maintenance,
            VehicleId = maintenance.Vehicle.Id,
            OrganizationId = organization.Id,
            Kind = "test",
            Format = "pdf",
            Title = "Тест",
            FileName = "test.pdf",
            StoragePath = "test/test.pdf",
            ContentType = "application/pdf",
            CreatedAt = createdAt
        };

    private static OrganizationMaterialStock CreateStock(Organization organization, string name, decimal quantity, decimal minimum) =>
        new()
        {
            Organization = organization,
            Name = name,
            Unit = "шт",
            QuantityOnHand = quantity,
            MinQuantity = minimum
        };

    private static OrganizationScenarioCatalogItem CreateRule(
        Organization organization,
        string code,
        string name,
        string? description,
        bool isActive = true,
        int sortOrder = 0) =>
        new()
        {
            Organization = organization,
            ScenarioCode = organization.PrimaryScenarioCode,
            KindCode = OrganizationScenarioCatalogKindCodes.PlanningRule,
            Code = code,
            Name = name,
            Description = description,
            SortOrder = sortOrder,
            IsActive = isActive
        };

    private static DateTime UtcDate(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static void AssertPlanningSummary(
        OrganizationWorkspacePlanningSummary summary,
        int overdue,
        int soon,
        int attention,
        int defects,
        int firstService,
        int counterUpdate)
    {
        Assert.Equal(overdue, summary.OverdueCount);
        Assert.Equal(soon, summary.SoonCount);
        Assert.Equal(attention, summary.AttentionCount);
        Assert.Equal(defects, summary.DefectsCount);
        Assert.Equal(firstService, summary.FirstServiceCount);
        Assert.Equal(counterUpdate, summary.CounterUpdateCount);
    }
}
