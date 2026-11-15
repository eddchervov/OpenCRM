using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Organizations;
using System.Net;
using OpenCRM.Modules.Clients;
using OpenCRM.Modules.Clients.Contracts;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Catalog;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Domain.Interfaces.Models;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Workspace.Queries;
using OpenCRM.Modules.Workspace.Workday;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Modules.Vehicles.Counters;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Modules.Inventory;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Subscriptions;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;
using OpenCRM.Testing;
using OpenCRM.WebApp.Controllers.Api.V1.Organizations;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class WorkspaceReadHttpAcceptanceTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    public static IEnumerable<object[]> OverviewRoleCases()
    {
        foreach (var parameters in CommercialOverviewRoleCases())
        {
            yield return [..parameters, true];
            yield return [..parameters, false];
        }
    }

    private static IEnumerable<object[]> CommercialOverviewRoleCases()
    {
        foreach (var scenario in new[]
        {
            OrganizationTypeCodes.Detailing,
            OrganizationTypeCodes.ServiceStation,
            OrganizationTypeCodes.Fleet
        })
        {
            foreach (var role in new[] { OrganizationRoleCodes.Owner, OrganizationRoleCodes.Employee, OrganizationRoleCodes.Viewer })
            {
                yield return [scenario, role, false, true, false];
            }
        }

        yield return [OrganizationTypeCodes.ServiceStation, OrganizationRoleCodes.Employee, true, true, false];
        yield return [OrganizationTypeCodes.ServiceStation, OrganizationRoleCodes.Employee, false, true, true];
        yield return [OrganizationTypeCodes.ServiceStation, OrganizationRoleCodes.Owner, false, false, false];
        yield return [OrganizationTypeCodes.Detailing, OrganizationRoleCodes.Owner, false, false, false];
        yield return [OrganizationTypeCodes.Detailing, OrganizationRoleCodes.Employee, false, false, false];
    }

    [Theory]
    [MemberData(nameof(OverviewRoleCases))]
    public async Task OverviewAndWorkdayKeepRealRoleAccessAndMaskRestrictedData(
        string scenarioCode,
        string roleCode,
        bool restrictPermissions,
        bool hasActiveSubscription,
        bool denyWork,
        bool commercialEnabled)
    {
        ApplicationUser actor;
        Organization organization;
        await using (var context = fixture.CreateContext())
        {
            await new OrganizationBillingTestServices.DefaultsInitializer(context).EnsureDefaultsAsync();
            var owner = new ApplicationUser();
            actor = roleCode == OrganizationRoleCodes.Owner ? owner : new ApplicationUser();
            organization = WorkspaceReadHttpAcceptanceTests.CreateOrganization(owner, "HTTP role matrix");
            organization.PrimaryScenarioCode = scenarioCode;
            if (actor != owner)
            {
                organization.Members.Add(new OrganizationMember { User = actor, RoleCode = roleCode });
            }

            var customer = new OrganizationClient { Organization = organization, Name = "Закрытый клиент", Phone = "+79990000123" };
            var vehicle = WorkspaceReadHttpAcceptanceTests.CreateVehicle(owner, organization);
            vehicle.OrganizationClient = customer;
            var archivedVehicle = WorkspaceReadHttpAcceptanceTests.CreateVehicle(owner, organization);
            archivedVehicle.IsArchived = true;
            var work = WorkspaceReadHttpAcceptanceTests.CreateMaintenance(vehicle, new DateTime(2026, 6, 10, 9, 0, 0, DateTimeKind.Utc));
            var enabledModules = new[] { Helpers.FeatureCodes.VehiclesModule, Helpers.FeatureCodes.ClientsModule,
                Helpers.FeatureCodes.MaintenanceModule, Helpers.FeatureCodes.DefectsModule, Helpers.FeatureCodes.CountersModule,
                Helpers.FeatureCodes.SchedulerModule };
            var definitions = await context.FeatureDefinitions.Where(definition => enabledModules.Contains(definition.Code)).ToListAsync();
            var plan = new SubscriptionPlan
            {
                Name = "HTTP role plan",
                PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
                DurationDays = 30,
                VehicleLimit = 20,
                Features = definitions.Select(definition => new SubscriptionPlanFeature
                {
                    FeatureDefinition = definition,
                    BoolValue = true
                }).ToList()
            };
            context.Organizations.Add(organization);
            context.Vehicles.AddRange(vehicle, archivedVehicle);
            context.Maintenance.Add(work);
            context.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Organization = organization,
                SubscriptionPlan = plan,
                StartDate = DateTime.UtcNow.AddDays(-30),
                EndDate = DateTime.UtcNow.AddDays(hasActiveSubscription ? 30 : -1),
                SourceCode = "acceptance"
            });
            var restrictedPermissions = new List<string>();
            if (restrictPermissions)
            {
                restrictedPermissions.AddRange([OrganizationPermissionCodes.AnalyticsView, OrganizationPermissionCodes.VehiclesView,
                    OrganizationPermissionCodes.ClientsView]);
            }

            if (denyWork)
            {
                restrictedPermissions.Add(OrganizationPermissionCodes.MaintenanceView);
            }

            if (restrictedPermissions.Count > 0)
            {
                foreach (var permission in restrictedPermissions)
                {
                    context.OrganizationRolePermissionOverrides.Add(new OrganizationRolePermissionOverride
                    {
                        Organization = organization,
                        RoleCode = roleCode,
                        PermissionCode = permission,
                        IsEnabled = false,
                        UpdatedByUserId = owner.Id
                    });
                }
            }

            await context.SaveChangesAsync();
        }

        using var host = await WorkspaceReadHttpAcceptanceTests.CreateHostAsync(fixture, actor, [organization.Id], true, commercialEnabled);
        using var client = host.GetTestServer().CreateClient();
        var overviewUrl = $"/api/v1/organizations/{organization.Id}/workspace/overview";
        using var anonymous = await client.GetAsync(overviewUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        using var response = await client.GetAsync(overviewUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;
        Assert.Equal(new[] { "organization", "workspaceAccess", "workspaceFeatures", "currentSubscription", "effectivePlan",
            "workspaceVehicleLimit", "workspaceMembersLimit", "activeVehiclesCount", "archivedVehiclesCount", "clientsCount",
            "metrics", "planning" }, root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(organization.Id, root.GetProperty("organization").GetProperty("id").GetGuid());
        var access = root.GetProperty("workspaceAccess");
        Assert.Equal(roleCode, access.GetProperty("roleCode").GetString());
        Assert.Equal(commercialEnabled, access.GetProperty("commercialEnabled").GetBoolean());
        Assert.Equal(commercialEnabled && hasActiveSubscription, access.GetProperty("hasActiveSubscription").GetBoolean());
        var isFreePlan = !commercialEnabled || !hasActiveSubscription
            && scenarioCode is OrganizationTypeCodes.Detailing or OrganizationTypeCodes.ServiceStation;

        var isReadOnly = roleCode == OrganizationRoleCodes.Viewer
            || commercialEnabled && !hasActiveSubscription && (!isFreePlan || roleCode != OrganizationRoleCodes.Owner);

        Assert.Equal(isFreePlan, access.GetProperty("isFreePlan").GetBoolean());
        Assert.Equal(isReadOnly, access.GetProperty("isReadOnly").GetBoolean());
        Assert.Equal(!commercialEnabled || hasActiveSubscription || isFreePlan, access.GetProperty("exportsEnabled").GetBoolean());
        if (hasActiveSubscription)
        {
            Assert.Equal(commercialEnabled && !restrictPermissions ? 20 : 0, root.GetProperty("workspaceVehicleLimit").GetInt32());
            if (commercialEnabled)
            {
                Assert.Equal("HTTP role plan", root.GetProperty("effectivePlan").GetProperty("name").GetString());
            }
            else
            {
                Assert.Equal(JsonValueKind.Null, root.GetProperty("effectivePlan").ValueKind);
            }
            Assert.Equal(restrictPermissions ? 0 : 1, root.GetProperty("activeVehiclesCount").GetInt32());
            Assert.Equal(restrictPermissions ? 0 : 1, root.GetProperty("archivedVehiclesCount").GetInt32());
            Assert.Equal(restrictPermissions ? 0 : 1, root.GetProperty("clientsCount").GetInt32());
            Assert.Equal(restrictPermissions ? 0 : 1, root.GetProperty("metrics").GetProperty("maintenanceTotalCount").GetInt32());
            foreach (var route in new[] { "workday?date=2026-06-10&timeZoneId=Asia/Novosibirsk", "visualization?date=2026-06-10" })
            {
                using var workdayResponse = await client.GetAsync($"/api/v1/organizations/{organization.Id}/workspace/{route}");
                if (denyWork)
                {
                    Assert.Equal(HttpStatusCode.Forbidden, workdayResponse.StatusCode);
                    continue;
                }

                Assert.Equal(HttpStatusCode.OK, workdayResponse.StatusCode);
                using var workday = JsonDocument.Parse(await workdayResponse.Content.ReadAsStringAsync());
                var item = Assert.Single(workday.RootElement.GetProperty("planned").GetProperty("items").EnumerateArray());
                Assert.Equal(restrictPermissions ? null : "Закрытый клиент", item.GetProperty("clientName").GetString());
                Assert.Equal(restrictPermissions ? null : "+79990000123", item.GetProperty("clientPhone").GetString());
            }
        }

        using var unavailable = await client.GetAsync($"/api/v1/organizations/{Guid.NewGuid()}/workspace/overview");
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
    }

    [Fact]
    public async Task MembershipAllowsAccessAndInvalidWorkdayArgumentsAreRejectedByHttp()
    {
        var (user, organization, _, _, _, _, _) = await this.SeedMaintenanceAsync();
        using var host = await WorkspaceReadHttpAcceptanceTests.CreateHostAsync(
            fixture, user, [organization.Id]);
        using var client = host.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        foreach (var route in new[] { "overview", "vehicle-options", "workday?date=2026-06-10" })
        {
            using var response = await client.GetAsync($"/api/v1/organizations/{organization.Id}/workspace/{route}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var missing = await client.GetAsync($"/api/v1/organizations/{Guid.NewGuid()}/workspace/{route}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using var invalidDate = await client.GetAsync($"/api/v1/organizations/{organization.Id}/workspace/workday?date=9999-12-31");
        Assert.Equal(HttpStatusCode.BadRequest, invalidDate.StatusCode);
        using var ignoredClientZone = await client.GetAsync($"/api/v1/organizations/{organization.Id}/workspace/workday?date=2026-06-10&timeZoneId=unknown-zone");
        Assert.Equal(HttpStatusCode.OK, ignoredClientZone.StatusCode);
    }

    [Theory]
    [InlineData("workspace/maintenance", "maintenanceDateTo")]
    [InlineData("workspace/maintenance", "maintenanceDateFrom")]
    [InlineData("workspace/maintenance-calendar", "dateTo")]
    [InlineData("workspace/maintenance-calendar", "dateFrom")]
    [InlineData("workspace/scheduler-tasks-calendar", "dateTo")]
    [InlineData("work-journal", "dateTo")]
    [InlineData("finance", "dateTo")]
    [InlineData("finance", "dateFrom")]
    public async Task WorkspaceDateRangesRejectMaximumDateWithoutServerError(string route, string parameter)
    {
        var (user, organization, _, _, _, _, _) = await this.SeedMaintenanceAsync();
        using var host = await WorkspaceReadHttpAcceptanceTests.CreateHostAsync(fixture, user, [organization.Id]);
        using var client = host.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        using var response = await client.GetAsync(
            $"/api/v1/organizations/{organization.Id}/{route}?{parameter}=9999-12-31");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Дата периода должна быть раньше 31.12.9999.", payload.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task WorkspaceVehicleOptionsKeepAuthorizationScopeAndJsonContract()
    {
        var (user, organization, foreignOrganization, earliest, _, _, _) = await this.SeedMaintenanceAsync();
        Guid vehicleId;
        await using (var context = fixture.CreateContext())
        {
            vehicleId = await context.Maintenance.Where(work => work.Id == earliest)
                .Select(work => work.VehicleId).SingleAsync();
        }

        using var host = await WorkspaceReadHttpAcceptanceTests.CreateHostAsync(fixture, user, [organization.Id]);
        using var client = host.GetTestServer().CreateClient();
        using var anonymous = await client.GetAsync($"/api/v1/organizations/{organization.Id}/workspace/vehicle-options");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        using var response = await client.GetAsync($"/api/v1/organizations/{organization.Id}/workspace/vehicle-options?take=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(payload.RootElement.EnumerateArray(), option => option.GetProperty("id").GetGuid() == vehicleId);
        foreach (var option in payload.RootElement.EnumerateArray())
        {
            Assert.Equal(new[] { "id", "displayName", "subtitle", "currentMileage", "mileageUnit", "registrationPlate" },
                option.EnumerateObject().Select(property => property.Name));
        }

        using var foreign = await client.GetAsync($"/api/v1/organizations/{foreignOrganization.Id}/workspace/vehicle-options");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task WorkspaceMaintenanceRouteUsesPostgresQueryAndHidesOrganizationsOutsideUserOverview()
    {
        var (user, organization, otherOrganization, earliest, latest, completedWorkId, _) = await this.SeedMaintenanceAsync();
        using var host = await WorkspaceReadHttpAcceptanceTests.CreateHostAsync(
            fixture,
            user,
            [organization.Id]);
        using var client = host.GetTestServer().CreateClient();

        using var anonymous = await client.GetAsync(
            $"/api/v1/organizations/{organization.Id}/workspace/maintenance?maintenanceDateFrom=2026-06-10&maintenanceDateTo=2026-06-10&maintenancePage=1&maintenancePageSize=10");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        using var response = await client.GetAsync(
            $"/api/v1/organizations/{organization.Id}/workspace/maintenance?maintenanceDateFrom=2026-06-10&maintenanceDateTo=2026-06-10&maintenancePage=1&maintenancePageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(
            payload.RootElement.GetProperty("totalCount").GetInt32() == 11,
            $"Ответ рабочего дня: {payload.RootElement.GetRawText()}");
        Assert.Equal(1, payload.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(10, payload.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(earliest, payload.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());

        using var foreignOrganization = await client.GetAsync(
            $"/api/v1/organizations/{otherOrganization.Id}/workspace/maintenance?maintenanceDateFrom=2026-06-10&maintenanceDateTo=2026-06-10");
        Assert.Equal(HttpStatusCode.NotFound, foreignOrganization.StatusCode);
        using var secondPage = await client.GetAsync(
            $"/api/v1/organizations/{organization.Id}/workspace/maintenance?maintenanceDateFrom=2026-06-10&maintenanceDateTo=2026-06-10&maintenancePage=2&maintenancePageSize=10");
        using var secondPagePayload = JsonDocument.Parse(await secondPage.Content.ReadAsStringAsync());
        Assert.Equal(2, secondPagePayload.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(1, secondPagePayload.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(latest, secondPagePayload.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());

        using var overviewResponse = await client.GetAsync($"/api/v1/organizations/{organization.Id}/workspace/overview");
        Assert.Equal(HttpStatusCode.OK, overviewResponse.StatusCode);
        using var overview = JsonDocument.Parse(await overviewResponse.Content.ReadAsStringAsync());
        Assert.Equal(12, overview.RootElement.GetProperty("metrics").GetProperty("maintenanceTotalCount").GetInt32());

        using var workdayResponse = await client.GetAsync(
            $"/api/v1/organizations/{organization.Id}/workspace/workday?date=2026-06-10&timeZoneId=UTC");
        Assert.Equal(HttpStatusCode.OK, workdayResponse.StatusCode);
        using var workday = JsonDocument.Parse(await workdayResponse.Content.ReadAsStringAsync());
        Assert.Equal(11, workday.RootElement.GetProperty("planned").GetProperty("totalCount").GetInt32());

        using var visualizationResponse = await client.GetAsync(
            $"/api/v1/organizations/{organization.Id}/workspace/visualization?date=2026-06-10");
        Assert.Equal(HttpStatusCode.OK, visualizationResponse.StatusCode);
        using var visualization = JsonDocument.Parse(await visualizationResponse.Content.ReadAsStringAsync());
        Assert.Equal(11, visualization.RootElement.GetProperty("planned").GetProperty("totalCount").GetInt32());

        using var calendarResponse = await client.GetAsync(
            $"/api/v1/organizations/{organization.Id}/workspace/maintenance-calendar?dateFrom=2026-06-10&dateTo=2026-06-10&includeAllStatuses=true");
        Assert.Equal(HttpStatusCode.OK, calendarResponse.StatusCode);
        using var calendar = JsonDocument.Parse(await calendarResponse.Content.ReadAsStringAsync());
        Assert.Equal(11, calendar.RootElement.GetArrayLength());

        using var maintenanceJournalResponse = await client.GetAsync(
            $"/api/v1/organizations/{organization.Id}/maintenance-journal?status=0&dateFrom=2026-06-10&dateTo=2026-06-10&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, maintenanceJournalResponse.StatusCode);
        using var maintenanceJournal = JsonDocument.Parse(await maintenanceJournalResponse.Content.ReadAsStringAsync());
        Assert.Equal(11, maintenanceJournal.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(10, maintenanceJournal.RootElement.GetProperty("items").GetArrayLength());

        using var workJournalResponse = await client.GetAsync(
            $"/api/v1/organizations/{organization.Id}/work-journal?dateFrom=2026-06-09&dateTo=2026-06-10&kind=maintenance&tab=works");
        Assert.Equal(HttpStatusCode.OK, workJournalResponse.StatusCode);
        using var workJournal = JsonDocument.Parse(await workJournalResponse.Content.ReadAsStringAsync());
        Assert.Equal("maintenance", workJournal.RootElement.GetProperty("selectedKind").GetString());
        Assert.Equal("works", workJournal.RootElement.GetProperty("selectedTab").GetString());
        Assert.Equal(1, workJournal.RootElement.GetProperty("workItemsCount").GetInt32());
        Assert.Equal(1, workJournal.RootElement.GetProperty("completedMaintenancesCount").GetInt32());
        var workJournalItem = Assert.Single(workJournal.RootElement.GetProperty("workItems").EnumerateArray());
        Assert.Equal("completed", workJournalItem.GetProperty("eventCode").GetString());
        Assert.Equal("Детейлинг кузова", workJournalItem.GetProperty("resultText").GetString());
        Assert.Equal(2500m, workJournalItem.GetProperty("price").GetDecimal());
        Assert.Equal($"#maintenance/{completedWorkId}", workJournalItem.GetProperty("detailUrl").GetString());

        using var financeResponse = await client.GetAsync(
            $"/api/v1/organizations/{organization.Id}/finance?dateFrom=2026-06-10&dateTo=2026-06-10");
        Assert.Equal(HttpStatusCode.OK, financeResponse.StatusCode);
        using var finance = JsonDocument.Parse(await financeResponse.Content.ReadAsStringAsync());
        Assert.Equal(11, finance.RootElement.GetProperty("kpis").GetProperty("ordersCount").GetInt32());

        using var statisticsResponse = await client.GetAsync($"/api/v1/organizations/{organization.Id}/workspace/statistics");
        Assert.Equal(HttpStatusCode.OK, statisticsResponse.StatusCode);
        using var statistics = JsonDocument.Parse(await statisticsResponse.Content.ReadAsStringAsync());
        Assert.Equal(11, statistics.RootElement.GetProperty("analytics").GetProperty("kpis").GetProperty("plannedWorks").GetInt32());

        using var counterVehiclesResponse = await client.GetAsync(
            $"/api/v1/organizations/{organization.Id}/counters/vehicles/1/12?sort=updated_desc");
        Assert.Equal(HttpStatusCode.OK, counterVehiclesResponse.StatusCode);
        using var counterVehicles = JsonDocument.Parse(await counterVehiclesResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, counterVehicles.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(42_000, counterVehicles.RootElement.GetProperty("items")[0].GetProperty("currentValue").GetInt32());

        using var counterHistoryResponse = await client.GetAsync(
            $"/api/v1/organizations/{organization.Id}/counters/history/1/12?sort=recorded_desc");
        Assert.Equal(HttpStatusCode.OK, counterHistoryResponse.StatusCode);
        using var counterHistory = JsonDocument.Parse(await counterHistoryResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, counterHistory.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(42_000, counterHistory.RootElement.GetProperty("items")[0].GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task MaterialsRouteReturnsOrganizationAggregatesAndFilteredPageFromPostgres()
    {
        var (user, organization, foreignOrganization) = await this.SeedStocksAsync();
        using var host = await WorkspaceReadHttpAcceptanceTests.CreateHostAsync(
            fixture,
            user,
            [organization.Id]);
        using var client = host.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        using var response = await client.GetAsync(
            $"/api/v1/organizations/{organization.Id}/materials?stockFilter=low&stocksPage=1&stocksPageSize=5&movementsPage=1&movementsPageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;
        Assert.Equal(1, root.GetProperty("lowStockCount").GetInt32());
        Assert.Equal(1, root.GetProperty("outOfStockCount").GetInt32());
        Assert.Equal(7m, root.GetProperty("totalQuantityOnHand").GetDecimal());
        Assert.Equal(40m, root.GetProperty("totalInventoryValue").GetDecimal());
        Assert.Equal(1, root.GetProperty("stocksTotalCount").GetInt32());
        Assert.Equal(1, root.GetProperty("stocks").GetArrayLength());
        Assert.Equal("Воск A", root.GetProperty("stocks")[0].GetProperty("name").GetString());

        using var foreignOrganizationResponse = await client.GetAsync(
            $"/api/v1/organizations/{foreignOrganization.Id}/materials");
        Assert.Equal(HttpStatusCode.NotFound, foreignOrganizationResponse.StatusCode);
    }

    [Fact]
    public async Task FreeWorkspaceWithoutAnalyticsFeatureCannotLoadStatisticsThroughHttp()
    {
        ApplicationUser user;
        Organization organization;
        await using (var context = fixture.CreateContext())
        {
            await new OrganizationBillingTestServices.DefaultsInitializer(context).EnsureDefaultsAsync();
            user = new ApplicationUser();
            organization = WorkspaceReadHttpAcceptanceTests.CreateOrganization(user, "Free statistics access");
            context.Users.Add(user);
            context.Organizations.Add(organization);
            await context.SaveChangesAsync();

            var freePlan = await context.SubscriptionPlans.SingleAsync(plan =>
                plan.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup);
            var analyticsDefinition = await context.FeatureDefinitions.SingleAsync(definition =>
                definition.Code == Helpers.FeatureCodes.AnalyticsModule);
            var analyticsFeature = await context.SubscriptionPlanFeatures.SingleAsync(feature =>
                feature.SubscriptionPlanId == freePlan.Id && feature.FeatureDefinitionId == analyticsDefinition.Id);
            Assert.NotEqual(true, analyticsFeature.BoolValue);
        }

        using var host = await WorkspaceReadHttpAcceptanceTests.CreateHostAsync(
            fixture, user, [organization.Id], useRealSubscriptionService: true);
        using var client = host.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        using var response = await client.GetAsync($"/api/v1/organizations/{organization.Id}/workspace/statistics");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Модуль статистики не подключен в тарифе организации.", payload.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ProWorkspaceWithDeniedAnalyticsPermissionCannotLoadStatisticsThroughHttp()
    {
        var (user, organization) = await this.SeedProAnalyticsWorkspaceAsync(denyAnalyticsPermission: true);
        using var host = await WorkspaceReadHttpAcceptanceTests.CreateHostAsync(
            fixture, user, [organization.Id], useRealSubscriptionService: true);
        using var client = host.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        using var response = await client.GetAsync($"/api/v1/organizations/{organization.Id}/workspace/statistics");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку.",
            payload.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task StatisticsEndpointDoesNotExposeForeignOrganization()
    {
        var (user, organization) = await this.SeedProAnalyticsWorkspaceAsync(denyAnalyticsPermission: false);
        ApplicationUser foreignUser;
        Organization foreignOrganization;
        await using (var context = fixture.CreateContext())
        {
            foreignUser = new ApplicationUser();
            foreignOrganization = WorkspaceReadHttpAcceptanceTests.CreateOrganization(foreignUser, "Foreign statistics access");
            context.Users.Add(foreignUser);
            context.Organizations.Add(foreignOrganization);
            await context.SaveChangesAsync();
        }

        using var host = await WorkspaceReadHttpAcceptanceTests.CreateHostAsync(
            fixture, user, [organization.Id], useRealSubscriptionService: true);
        using var client = host.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        using var response = await client.GetAsync($"/api/v1/organizations/{foreignOrganization.Id}/workspace/statistics");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ProOwnerWithAnalyticsPermissionCanLoadStatisticsThroughHttp()
    {
        var (user, organization) = await this.SeedProAnalyticsWorkspaceAsync(denyAnalyticsPermission: false);
        using var host = await WorkspaceReadHttpAcceptanceTests.CreateHostAsync(
            fixture, user, [organization.Id], useRealSubscriptionService: true);
        using var client = host.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        using var response = await client.GetAsync($"/api/v1/organizations/{organization.Id}/workspace/statistics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(payload.RootElement.GetProperty("analytics").TryGetProperty("kpis", out _));
    }

    [Fact]
    public async Task ViewerCanReadWorkJournalThroughHttpUsingRealPermissionAndSubscriptionServices()
    {
        var (_, organization, _, _, _, completedWorkId, viewer) = await this.SeedMaintenanceAsync();
        await using (var context = fixture.CreateContext())
        {
            var plan = new SubscriptionPlan
            {
                Name = "Workspace HTTP acceptance plan",
                PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
                DurationDays = 30,
                Price = 1000m,
                VehicleLimit = 20
            };
            context.SubscriptionPlans.Add(plan);
            context.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                OrganizationId = organization.Id,
                SubscriptionPlan = plan,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(30),
                SourceCode = "acceptance"
            });
            await context.SaveChangesAsync();
        }

        using var host = await WorkspaceReadHttpAcceptanceTests.CreateHostAsync(
            fixture,
            viewer,
            [organization.Id],
            useRealSubscriptionService: true);
        using var client = host.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        using var response = await client.GetAsync(
            $"/api/v1/organizations/{organization.Id}/work-journal?dateFrom=2026-06-09&dateTo=2026-06-10&kind=maintenance&tab=works");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var journal = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("maintenance", journal.RootElement.GetProperty("selectedKind").GetString());
        Assert.Equal(1, journal.RootElement.GetProperty("workItemsCount").GetInt32());
        Assert.Equal($"#maintenance/{completedWorkId}", journal.RootElement.GetProperty("workItems")[0].GetProperty("detailUrl").GetString());
        Assert.Equal("Детейлинг кузова", journal.RootElement.GetProperty("workItems")[0].GetProperty("resultText").GetString());
    }

    [Fact]
    public async Task WorkspaceCountersFinanceAnalyticsCalendarAndMaintenanceReadsExecuteOrganizationScopedSql()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = WorkspaceReadHttpAcceptanceTests.CreateOrganization(user, "Query acceptance");
        var foreignUser = new ApplicationUser();
        var foreignOrganization = WorkspaceReadHttpAcceptanceTests.CreateOrganization(foreignUser, "Foreign query acceptance");
        var vehicle = WorkspaceReadHttpAcceptanceTests.CreateVehicle(user, organization);
        vehicle.ManualBrandName = "BMW";
        vehicle.ManualModelName = "X3";
        vehicle.CurrentMileage = 150_000;
        vehicle.CurrentMileageUpdatedAt = new DateTime(2026, 6, 10, 8, 0, 0, DateTimeKind.Utc);
        var vehicleWithoutCounter = WorkspaceReadHttpAcceptanceTests.CreateVehicle(user, organization);
        vehicleWithoutCounter.ManualBrandName = "Audi";
        vehicleWithoutCounter.ManualModelName = "A4";
        var foreignVehicle = WorkspaceReadHttpAcceptanceTests.CreateVehicle(foreignUser, foreignOrganization);
        var date = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        vehicleWithoutCounter.DateTimeCreation = date.AddDays(-20);
        var planned = WorkspaceReadHttpAcceptanceTests.CreateMaintenance(vehicle, date.AddDays(-10));
        planned.Status = MaintenanceStatus.Planned;
        planned.CompletedDate = null;
        planned.Details = [new() { Name = "Диагностика", Price = 1000m }];
        var completed = WorkspaceReadHttpAcceptanceTests.CreateMaintenance(vehicle, date);
        completed.Status = MaintenanceStatus.Completed;
        completed.CompletedDate = date;
        completed.Mileage = 149_000;
        completed.DiscountAmount = 100m;
        completed.PaidAmount = 500m;
        completed.MaterialCostAmount = 100m;
        completed.LaborCostAmount = 200m;
        completed.Details = [new() { Name = "Полировка", Price = 1000m }, new() { Name = "Химчистка", Price = 500m }];
        var foreignCompleted = WorkspaceReadHttpAcceptanceTests.CreateMaintenance(foreignVehicle, date);
        foreignCompleted.Status = MaintenanceStatus.Completed;
        foreignCompleted.CompletedDate = date;
        var deleted = WorkspaceReadHttpAcceptanceTests.CreateMaintenance(vehicle, date.AddDays(-2));
        deleted.IsDeleted = true;
        var historyEvent = new MaintenanceHistoryEvent
        {
            Maintenance = completed,
            VehicleId = vehicle.Id,
            UserId = user.Id,
            OrganizationId = organization.Id,
            EventType = "fields_changed",
            Title = "Обновлена карточка",
            CreatedAt = date.AddHours(2)
        };
        var counter = new VehicleCounterEntry
        {
            Vehicle = vehicle,
            User = user,
            Value = 149_500,
            RecordedAt = date.AddHours(1),
            Comment = "Контрольная запись"
        };
        var foreignCounter = new VehicleCounterEntry
        {
            Vehicle = foreignVehicle,
            User = foreignUser,
            Value = 900,
            RecordedAt = date.AddHours(2)
        };
        context.Users.AddRange(user, foreignUser);
        context.Organizations.AddRange(organization, foreignOrganization);
        context.Vehicles.AddRange(vehicle, vehicleWithoutCounter, foreignVehicle);
        context.Maintenance.AddRange(planned, completed, foreignCompleted, deleted);
        context.VehicleCounterEntries.AddRange(counter, foreignCounter);
        context.MaintenanceHistoryEvents.Add(historyEvent);
        await context.SaveChangesAsync();

        var subscription = WorkspaceReadHttpAcceptanceTests.CreatePermissionSubscription(fixture);
        var counterQueries = new VehicleCounterQueryService(new VehicleRepository(context), context, subscription.Object);
        var vehiclePage = await counterQueries.GetCounterVehiclesPageAsync(user.Id, organization.Id, page: 1, pageSize: 1, sort: "name_asc");
        Assert.Equal(2, vehiclePage.AllCount);
        Assert.Equal(2, vehiclePage.TotalCount);
        Assert.Equal(1, vehiclePage.VehiclesWithCountersCount);
        Assert.Equal(1, vehiclePage.PageSize);
        Assert.True(vehiclePage.HasMore);
        Assert.Equal(vehicleWithoutCounter.Id, Assert.Single(vehiclePage.Items).VehicleId);

        var historyPage = await counterQueries.GetCounterHistoryPageAsync(user.Id, organization.Id, page: 1, pageSize: 1);
        Assert.Equal(1, historyPage.TotalCount);
        Assert.Equal(counter.Id, Assert.Single(historyPage.Items).Id);
        var timeline = await counterQueries.GetCounterTimelineAsync(user.Id, organization.Id, vehicle.Id);
        Assert.Contains(timeline, item => item.TimelineType == "counter" && item.Id == counter.Id);
        Assert.Contains(timeline, item => item.TimelineType == "maintenance" && item.Value == 149_000);
        var maintenanceServices = new ServiceCollection();
        maintenanceServices.AddSingleton(context);
        maintenanceServices.AddSingleton<IMaintenanceRepository>(new MaintenanceRepository(context));
        maintenanceServices.AddSingleton<IMaintenancePhotoRepository>(
            new MaintenancePhotoRepository(context, Mock.Of<ILogger<MaintenancePhotoRepository>>()));
        maintenanceServices.AddSingleton<IVehicleMaintenanceLookupService>(
            new VehicleMaintenanceLookupService(new VehicleRepository(context)));
        maintenanceServices.AddSingleton(subscription.Object);
        maintenanceServices.AddSingleton(Mock.Of<IOrganizationInquiryOriginReader>());
        maintenanceServices.AddOpenCRMMaintenance();
        using var maintenanceProvider = maintenanceServices.BuildServiceProvider();
        var maintenanceQueries = maintenanceProvider.GetRequiredService<IMaintenanceQueryService>();
        var maintenancePage = await maintenanceQueries.Get(user.Id, organization.Id, vehicle.Id, page: 1, pageSize: 1, sort: "date_desc");
        Assert.Equal(2, maintenancePage.TotalCount);
        Assert.Equal(1, maintenancePage.PageSize);
        Assert.Equal(completed.Id, Assert.Single(maintenancePage.Items).Id);
        var deletedPage = await maintenanceQueries.GetDeleted(user.Id, organization.Id, vehicle.Id);
        Assert.Equal(deleted.Id, Assert.Single(deletedPage.Items).Id);
        var maintenanceDetails = await maintenanceQueries.GetById(user.Id, organization.Id, completed.Id);
        Assert.Equal(completed.Id, maintenanceDetails.Id);
        Assert.Equal(2, maintenanceDetails.MaintenanceDetails.Count());
        Assert.Equal(historyEvent.Id, Assert.Single(await maintenanceQueries.GetHistory(user.Id, organization.Id, completed.Id)).Id);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => maintenanceQueries.GetById(foreignUser.Id, organization.Id, completed.Id));

        var workspaceQueries = WorkspaceReadHttpAcceptanceTests.CreateWorkspaceQueryService(context);
        var metrics = await workspaceQueries.GetOverviewMetricsAsync(organization.Id, hasDefectsModule: false, hasCountersModule: true);
        Assert.Equal(2, metrics.MaintenanceTotalCount);
        Assert.Equal(1, metrics.MaintenancePlannedCount);
        Assert.Equal(1, metrics.MaintenanceCompletedCount);
        Assert.Equal(1, metrics.CounterEntriesCount);

        var finance = await workspaceQueries.GetFinanceSummaryAsync(organization.Id, date.Date, date.Date);
        Assert.Equal(1, finance.Kpis.OrdersCount);
        Assert.Equal(1500m, finance.Kpis.GrossAmount);
        Assert.Equal(1400m, finance.Kpis.NetAmount);
        Assert.Equal(500m, finance.Kpis.PaidAmount);
        Assert.Equal(900m, finance.Kpis.DebtAmount);
        Assert.Equal(1100m, finance.Kpis.ProfitAmount);

        var analytics = await workspaceQueries.GetAnalyticsAsync(
            organization.Id,
            hasClientsModule: false,
            hasDefectsModule: false,
            hasCountersModule: true,
            hasMaterialsModule: false,
            today: date.Date);
        Assert.Equal(2, analytics.Kpis.ActiveVehicles);
        Assert.Equal(1, analytics.Kpis.PlannedWorks);
        Assert.Equal(1, analytics.Kpis.CompletedWorks14Days);
        Assert.Equal(1, analytics.Kpis.CounterEntries14Days);
        var planning = await workspaceQueries.GetPlanningSummaryAsync(
            organization.Id,
            hasDefectsModule: false,
            hasCountersModule: true,
            hasSchedulerModule: true,
            today: date.Date);
        Assert.Equal(1, planning.FirstServiceCount);

        var calendar = await workspaceQueries.GetMaintenanceCalendarAsync(organization.Id, date.Date, date.Date, includeAllStatuses: true);
        Assert.Equal(completed.Id, Assert.Single(calendar).Id);
        var workJournal = await workspaceQueries.GetWorkJournalAsync(organization.Id, date.Date, date.Date, null, "maintenance", "works", false);
        Assert.Equal(1, workJournal.CompletedMaintenancesCount);
        Assert.Equal($"#maintenance/{completed.Id}", Assert.Single(workJournal.WorkItems).DetailUrl);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => counterQueries.GetCounterHistoryAsync(foreignUser.Id, organization.Id));
    }

    private async Task<(ApplicationUser User, Organization Organization)> SeedProAnalyticsWorkspaceAsync(bool denyAnalyticsPermission)
    {
        await using var context = fixture.CreateContext();
        await new OrganizationBillingTestServices.DefaultsInitializer(context).EnsureDefaultsAsync();
        var owner = new ApplicationUser();
        var user = denyAnalyticsPermission ? new ApplicationUser() : owner;
        var organization = WorkspaceReadHttpAcceptanceTests.CreateOrganization(owner, "PRO statistics access");
        if (user != owner)
        {
            organization.Members.Add(new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Employee });
        }

        var analyticsDefinition = await context.FeatureDefinitions.SingleAsync(definition =>
            definition.Code == Helpers.FeatureCodes.AnalyticsModule);
        var plan = new SubscriptionPlan
        {
            Name = $"PRO statistics {Guid.NewGuid():N}",
            Group = "PRO",
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
            DurationDays = 30,
            Price = 1000m,
            VehicleLimit = 20,
            Features = [new SubscriptionPlanFeature { FeatureDefinition = analyticsDefinition, BoolValue = true }]
        };
        context.Organizations.Add(organization);
        context.SubscriptionPlans.Add(plan);
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Organization = organization,
            SubscriptionPlan = plan,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(30),
            SourceCode = "acceptance"
        });
        if (denyAnalyticsPermission)
        {
            context.OrganizationRolePermissionOverrides.Add(new OrganizationRolePermissionOverride
            {
                Organization = organization,
                RoleCode = OrganizationRoleCodes.Employee,
                PermissionCode = OrganizationPermissionCodes.AnalyticsView,
                IsEnabled = false,
                UpdatedByUserId = owner.Id
            });
        }

        await context.SaveChangesAsync();

        return (user, organization);
    }

    private async Task<(ApplicationUser User, Organization Organization, Organization OtherOrganization, Guid Earliest, Guid Latest, Guid CompletedWorkId, ApplicationUser Viewer)> SeedMaintenanceAsync()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = WorkspaceReadHttpAcceptanceTests.CreateOrganization(user, "HTTP workspace A");
        var otherUser = new ApplicationUser();
        var otherOrganization = WorkspaceReadHttpAcceptanceTests.CreateOrganization(otherUser, "HTTP workspace B");
        var viewer = new ApplicationUser();
        organization.Members.Add(new OrganizationMember { User = viewer, RoleCode = OrganizationRoleCodes.Viewer });
        var vehicle = WorkspaceReadHttpAcceptanceTests.CreateVehicle(user, organization);
        var otherVehicle = WorkspaceReadHttpAcceptanceTests.CreateVehicle(otherUser, otherOrganization);
        var date = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
        vehicle.CurrentMileage = 42_000;
        vehicle.CurrentMileageUpdatedAt = date.AddHours(10);
        var works = Enumerable.Range(0, 11)
            .Select(minute => WorkspaceReadHttpAcceptanceTests.CreateMaintenance(vehicle, date.AddHours(9).AddMinutes(minute)))
            .ToArray();
        var completedWork = WorkspaceReadHttpAcceptanceTests.CreateMaintenance(vehicle, date.AddDays(-1));
        completedWork.Status = MaintenanceStatus.Completed;
        completedWork.CompletedDate = date.AddDays(-1);
        completedWork.Details = [new() { Name = "Детейлинг кузова", Price = 2500m }];
        var foreign = WorkspaceReadHttpAcceptanceTests.CreateMaintenance(otherVehicle, date.AddHours(9));
        var counter = new VehicleCounterEntry
        {
            Vehicle = vehicle,
            User = user,
            Value = 42_000,
            RecordedAt = date.AddHours(10)
        };
        context.Users.AddRange(user, otherUser, viewer);
        context.Organizations.AddRange(organization, otherOrganization);
        context.Maintenance.AddRange(works);
        context.Maintenance.Add(completedWork);
        context.Maintenance.Add(foreign);
        context.VehicleCounterEntries.Add(counter);
        await context.SaveChangesAsync();

        return (user, organization, otherOrganization, works[0].Id, works[^1].Id, completedWork.Id, viewer);
    }

    private async Task<(ApplicationUser User, Organization Organization, Organization ForeignOrganization)> SeedStocksAsync()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = WorkspaceReadHttpAcceptanceTests.CreateOrganization(user, "HTTP inventory A");
        var foreignUser = new ApplicationUser();
        var foreignOrganization = WorkspaceReadHttpAcceptanceTests.CreateOrganization(foreignUser, "HTTP inventory B");
        context.Users.AddRange(user, foreignUser);
        context.Organizations.AddRange(organization, foreignOrganization);
        context.OrganizationMaterialStocks.AddRange(
            WorkspaceReadHttpAcceptanceTests.CreateStock(organization, "Воск A", 2m, 3m, 10m, 20m),
            WorkspaceReadHttpAcceptanceTests.CreateStock(organization, "Воск B", 0m, 1m, 8m, 0m),
            WorkspaceReadHttpAcceptanceTests.CreateStock(organization, "Полироль", 5m, 2m, 4m, 20m),
            WorkspaceReadHttpAcceptanceTests.CreateStock(foreignOrganization, "Чужой запас", 100m, 0m, 10m, 1000m));
        await context.SaveChangesAsync();

        return (user, organization, foreignOrganization);
    }

    private static async Task<IHost> CreateHostAsync(
        NotificationFixture fixture,
        ApplicationUser user,
        IReadOnlyCollection<Guid> visibleOrganizationIds,
        bool useRealSubscriptionService = false,
        bool commercialEnabled = true)
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            Options.Create(new IdentityOptions()),
            Mock.Of<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<ApplicationUser>>>());
        userManager.Setup(manager => manager.GetUserAsync(It.Is<ClaimsPrincipal>(principal =>
                principal.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id)))
            .ReturnsAsync(user);
        userManager.Setup(manager => manager.GetUserAsync(It.Is<ClaimsPrincipal>(principal =>
                principal.FindFirstValue(ClaimTypes.NameIdentifier) != user.Id)))
            .ReturnsAsync((ApplicationUser?)null);

        var organizationTypes = new Mock<IOrganizationTypeCatalog>();
        var organizationService = new Mock<IOrganizationQueryService>();
        organizationService.Setup(service => service.GetOverviewAsync(user.Id)).ReturnsAsync(new TeamOverviewDto
        {
            Organizations = visibleOrganizationIds.Select(id => new OrganizationDto { Id = id }).ToArray()
        });
        organizationService.Setup(service => service.GetByIdAsync(user.Id, It.IsAny<Guid>()))
            .ReturnsAsync((string _, Guid id) => new OrganizationDto { Id = id });
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(service => service.EnsureWorkspaceCanReadAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string>()))
            .Returns<string, Guid, string>(async (userId, organizationId, permissionCode) =>
            {
                await using var context = fixture.CreateContext();
                var permissionService = new OrganizationPermissionService(context, Mock.Of<IOrganizationAuditService>());
                await permissionService.EnsurePermissionAsync(userId, organizationId, permissionCode);
            });
        subscription.Setup(service => service.GetWorkspaceAccessAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new OrganizationWorkspaceAccessDto());
        subscription.Setup(service => service.GetFeatureSnapshotAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new FeatureAccessSnapshotDto
            {
                Features = Helpers.FeatureCodes.All.Select(code => new FeatureValueDto
                {
                    Code = code,
                    BoolValue = true
                }).ToList()
            });
        subscription.Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        subscription.Setup(service => service.GetVehicleLimitAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>()))
            .ReturnsAsync(100);
        subscription.Setup(service => service.GetMembersLimitAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>()))
            .ReturnsAsync(100);
        subscription.Setup(service => service.HasActiveSubscriptionAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(true);
        subscription.Setup(service => service.GetCurrentAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync((OrganizationSubscriptionDto?)null);
        subscription.Setup(service => service.GetEffectivePlanAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync((SubscriptionPlanDto?)null);
        var now = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc);
        var systemClock = Mock.Of<OpenCRM.Domain.Interfaces.ISystemClock>(clock =>
            clock.UtcNow == now && clock.UtcToday == now.Date);

        var hostBuilder = new HostBuilder()
            .ConfigureAppConfiguration((_, configuration) => configuration.AddJsonFile(
                Path.Combine(AppContext.BaseDirectory, "workspace-appsettings.json"))
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PlatformPolicy:Commercial:Enabled"] = commercialEnabled.ToString()
                }))
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddScoped(_ => fixture.CreateContext());
                    services.AddAuthentication("WorkspaceAcceptance")
                        .AddScheme<AuthenticationSchemeOptions, WorkspaceAcceptanceAuthenticationHandler>(
                            "WorkspaceAcceptance",
                            _ => { });
                    services.AddAuthorization();
                    services.AddControllers().AddApplicationPart(typeof(OrganizationWorkspaceApiController).Assembly);
                    services.AddSingleton(user);
                    services.AddSingleton(userManager.Object);
                    services.AddSingleton(organizationTypes.Object);
                    services.AddSingleton(organizationService.Object);
                    services.AddSingleton(Mock.Of<IOrganizationProfileCommandService>());
                    services.AddSingleton(Mock.Of<IOrganizationArchiveService>());
                    services.AddSingleton(Mock.Of<IOrganizationMembershipService>());
                    services.AddSingleton(Mock.Of<IOrganizationInvitationCommandService>());
                    services.AddSingleton(Mock.Of<IOrganizationAuditService>());
                    services.AddScoped<IOrganizationPermissionService>(provider => new OrganizationPermissionService(
                        provider.GetRequiredService<OpenCRMContext>(),
                        provider.GetRequiredService<IOrganizationAuditService>()));
                    services.AddScoped<IFeatureDefaultsInitializer>(provider => new OrganizationBillingTestServices.DefaultsInitializer(
                        provider.GetRequiredService<OpenCRMContext>()));
                    services.AddOpenCRMBilling();
                    services.AddOpenCRMWorkspace();
                    // Справка организации вынесена из платформы: контроллер получает статьи через отдельный провайдер.
                    services.AddSingleton(Mock.Of<OpenCRM.WebApp.Models.Organizations.IOrganizationWikiContentProvider>());
                    services.AddSingleton(Mock.Of<IOrganizationWorkspaceShellService>());
                    services.AddSingleton(Mock.Of<ISchedulerQueryService>());
                    services.AddSingleton(Mock.Of<ISchedulerTaskCommandService>());
                    services.AddSingleton(Mock.Of<ISchedulerRecurrenceService>());
                    services.AddSingleton(Mock.Of<ICatalogQueryService>());
                    services.AddSingleton(Mock.Of<ICatalogCommandService>());
                    services.AddSingleton(Mock.Of<ICatalogInitializationService>());
                    services.AddSingleton(Mock.Of<ICatalogMaterialLookup>());
                    services.AddSingleton(systemClock);
                    services.AddSingleton(Mock.Of<OpenCRM.Modules.Payments.Contracts.IPaymentCreationGateway>());
                    services.AddSingleton(Mock.Of<IPromoCodeService>());

                    services.AddSingleton(
                        Mock.Of<OpenCRM.Modules.Promotions.Contracts.IPromotionPaymentCoordinator>(MockBehavior.Strict));

                    services.AddScoped<IOrganizationSubscriptionService>(provider => useRealSubscriptionService
                        ? new OrganizationSubscriptionService(
                            provider.GetRequiredService<IOrganizationSubscriptionCatalogQueryService>(),
                            provider.GetRequiredService<IOrganizationEntitlementService>(),
                            provider.GetRequiredService<IWorkspaceAccessService>(),
                            provider.GetRequiredService<IOrganizationSubscriptionCheckoutService>(),
                            provider.GetRequiredService<IOrganizationSubscriptionExtensionQueryService>(),
                            provider.GetRequiredService<IOrganizationSubscriptionCommandService>(),
                            provider.GetRequiredService<IOrganizationSubscriptionActivationService>(),
                            provider.GetRequiredService<IOrganizationSubscriptionPurchasePolicyService>())
                        : subscription.Object);
                    services.AddScoped<IVehicleRepository, VehicleRepository>();
                    services.AddScoped<IModelRepository, ModelRepository>();
                    services.AddScoped<IVehiclePhotoRepository>(_ => Mock.Of<IVehiclePhotoRepository>());
                    services.AddScoped<IVehicleCatalogSuggestionRepository, VehicleCatalogSuggestionRepository>();
                    services.AddSingleton(Mock.Of<OpenCRM.Modules.Billing.Contracts.IPersonalSubscriptionQueryService>());
                    services.AddSingleton(Mock.Of<IFeatureAccessService>());
                    services.AddOpenCRMVehicles();
                    services.AddOpenCRMClients();
                    services.AddOpenCRMMaintenance();
                    services.AddOpenCRMInventory();
                });
                webHost.Configure(app =>
                {
                    app.UseMiddleware<OpenCRM.WebApp.Infrastructure.Api.ApiExceptionHandlingMiddleware>();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            });

        return await hostBuilder.StartAsync();
    }

    private static OrganizationWorkspaceQueryService CreateWorkspaceQueryService(OpenCRMContext context)
    {
        return new OrganizationWorkspaceQueryService(
            new OrganizationWorkdayQueryService(context),
            new OrganizationWorkspaceMaintenanceQueryService(context),
            new OrganizationFinanceQueryService(context),
            new OrganizationMaintenanceJournalQueryService(context),
            new OrganizationWorkJournalQueryService(context),
            new OrganizationWorkspaceAnalyticsQueryService(context),
            new OrganizationWorkspacePlanningQueryService(context));
    }

    private static Mock<IOrganizationSubscriptionService> CreatePermissionSubscription(NotificationFixture fixture)
    {
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(service => service.EnsureWorkspaceCanReadAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string>()))
            .Returns<string, Guid, string>(async (userId, organizationId, permissionCode) =>
            {
                await using var context = fixture.CreateContext();
                var permissionService = new OrganizationPermissionService(context, Mock.Of<IOrganizationAuditService>());
                await permissionService.EnsurePermissionAsync(userId, organizationId, permissionCode);
            });

        return subscription;
    }

    private static Organization CreateOrganization(ApplicationUser user, string name)
    {
        return new Organization
        {
            Name = name,
            OwnerUser = user,
            PrimaryScenarioCode = OrganizationTypeCodes.Detailing,
            Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
        };
    }

    private static Vehicle CreateVehicle(ApplicationUser user, Organization organization)
    {
        return new Vehicle
        {
            User = user,
            Organization = organization,
            ManualBrandName = "Марка",
            ManualModelName = "Модель",
            RegistrationPlate = "А123АА154"
        };
    }

    private static MaintenanceEntity CreateMaintenance(Vehicle vehicle, DateTime date)
    {
        return new MaintenanceEntity
        {
            User = vehicle.User,
            Vehicle = vehicle,
            Date = date,
            PlannedDate = date,
            PlannedStartTime = date.TimeOfDay,
            PlannedEndTime = date.TimeOfDay.Add(TimeSpan.FromHours(1)),
            DateTimeCreation = date,
            Type = MaintenanceType.Other,
            Status = MaintenanceStatus.Planned,
            Details = [new() { Name = "Полировка", Price = 1000m }]
        };
    }

    private static OrganizationMaterialStock CreateStock(
        Organization organization,
        string name,
        decimal quantity,
        decimal minimum,
        decimal unitCost,
        decimal inventoryValue)
    {
        return new OrganizationMaterialStock
        {
            Organization = organization,
            Name = name,
            Unit = "шт",
            QuantityOnHand = quantity,
            MinQuantity = minimum,
            AverageUnitCost = unitCost,
            InventoryValue = inventoryValue
        };
    }

    private sealed class WorkspaceAcceptanceAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApplicationUser user) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!this.Request.Headers.TryGetValue("X-Test-Authenticated", out var value) || value != "true")
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id)],
                this.Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
