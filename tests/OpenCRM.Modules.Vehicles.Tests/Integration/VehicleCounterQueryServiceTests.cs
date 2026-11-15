using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Vehicles.Counters;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehicleCounterQueryServiceTests(VehicleDatabaseFixture fixture) : IClassFixture<VehicleDatabaseFixture>
{
    [Fact]
    public async Task CounterQueriesApplyScopeAndMergeCounterHistoryWithMaintenance()
    {
        await using var context = fixture.CreateContext();
        var scenario = await SeedCounterScenarioAsync(context);

        var personalVehicles = await scenario.Service.GetCounterVehiclesAsync(scenario.User.Id, null);

        Assert.Equal(4, personalVehicles.Count);
        Assert.Contains(personalVehicles, vehicle => vehicle.VehicleId == scenario.Camry.Id && vehicle.CurrentValue == 120_000);
        Assert.DoesNotContain(personalVehicles, vehicle =>
            vehicle.VehicleId == scenario.Archived.Id
            || vehicle.VehicleId == scenario.Deleted.Id
            || vehicle.VehicleId == scenario.Foreign.Id);
        var organizationVehicles = await scenario.Service.GetCounterVehiclesAsync(scenario.User.Id, scenario.Organization.Id);
        Assert.Equal(scenario.FleetVehicle.Id, Assert.Single(organizationVehicles).VehicleId);

        var history = await scenario.Service.GetCounterHistoryAsync(scenario.User.Id, null);
        Assert.Equal(6, history.Count);
        Assert.Equal(scenario.HondaMaintenanceEntry.Id, history[0].Id);
        Assert.Equal(scenario.MaintenanceEntry.Id, history[1].Id);
        Assert.Equal(scenario.Latest.Id, history[2].Id);
        Assert.Equal(scenario.Earlier.Id, history[3].Id);
        Assert.Single(await scenario.Service.GetCounterHistoryAsync(scenario.User.Id, null, scenario.Camry.Id, 1));
        Assert.Empty(await scenario.Service.GetCounterHistoryAsync(scenario.User.Id, null, take: 0));

        var timeline = await scenario.Service.GetCounterTimelineAsync(scenario.User.Id, null, scenario.Camry.Id);
        Assert.Equal(4, timeline.Count);
        Assert.Equal("maintenance", timeline[^1].TimelineType);
        Assert.Equal((int)MaintenanceType.Repair, timeline[^1].MaintenanceType);
        Assert.False(timeline[^1].CanManage);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Service.GetCounterTimelineAsync(scenario.User.Id, null, Guid.NewGuid()));
        scenario.Access.Verify(service => service.EnsureWorkspaceCanReadAsync(
            scenario.User.Id,
            scenario.Organization.Id,
            OrganizationPermissionCodes.CountersView), Times.AtLeastOnce);
    }

    [Fact]
    public async Task CounterVehiclePagesApplyFiltersOrderingAndPaging()
    {
        await using var context = fixture.CreateContext();
        var scenario = await SeedCounterScenarioAsync(context);

        var descendingVehicles = await scenario.Service.GetCounterVehiclesPageAsync(
            scenario.User.Id,
            null,
            page: 1,
            pageSize: 10,
            sort: "updated_desc");
        Assert.Equal(4, descendingVehicles.AllCount);
        Assert.Equal(1, descendingVehicles.VehiclesWithCountersCount);
        Assert.Equal(scenario.Camry.Id, descendingVehicles.Items.First().VehicleId);

        var ascendingVehicles = await scenario.Service.GetCounterVehiclesPageAsync(
            scenario.User.Id,
            null,
            page: 0,
            pageSize: 0,
            sort: "updated_asc");
        Assert.Equal(1, ascendingVehicles.Page);
        Assert.Equal(12, ascendingVehicles.PageSize);
        Assert.Equal(scenario.Camry.Id, ascendingVehicles.Items.First().VehicleId);

        var searchedVehicles = await scenario.Service.GetCounterVehiclesPageAsync(
            scenario.User.Id,
            null,
            searchQuery: "  camry  ");
        Assert.Equal(scenario.Camry.Id, Assert.Single(searchedVehicles.Items).VehicleId);
        var vehicleIdPage = await scenario.Service.GetCounterVehiclesPageAsync(
            scenario.User.Id,
            null,
            vehicleId: scenario.Honda.Id);
        Assert.Equal(scenario.Honda.Id, Assert.Single(vehicleIdPage.Items).VehicleId);

        var nameDescendingVehicles = await scenario.Service.GetCounterVehiclesPageAsync(
            scenario.User.Id,
            null,
            sort: "name_desc");
        Assert.Equal(scenario.BlankVehicle.Id, nameDescendingVehicles.Items.First().VehicleId);
        var defaultVehicles = await scenario.Service.GetCounterVehiclesPageAsync(
            scenario.User.Id,
            null,
            searchQuery: "   ",
            sort: "unsupported");
        Assert.Equal(scenario.CatalogVehicle.Id, defaultVehicles.Items.First().VehicleId);

        var emptyVehicles = await scenario.Service.GetCounterVehiclesPageAsync(Guid.NewGuid().ToString(), null);
        Assert.Empty(emptyVehicles.Items);
        Assert.False(emptyVehicles.HasMore);
        var organizationVehiclePage = await scenario.Service.GetCounterVehiclesPageAsync(
            scenario.User.Id,
            scenario.Organization.Id,
            organizationClientId: scenario.Client.Id,
            searchQuery: "КЛИЕНТ ПАРКА");
        Assert.Equal(scenario.FleetVehicle.Id, Assert.Single(organizationVehiclePage.Items).VehicleId);
    }

    [Fact]
    public async Task CounterHistoryPagesApplyFiltersOrderingAndTimelineSearch()
    {
        await using var context = fixture.CreateContext();
        var scenario = await SeedCounterScenarioAsync(context);

        var globalHistoryPage = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            page: 1,
            pageSize: 1,
            sort: "value_desc");
        Assert.Equal(6, globalHistoryPage.AllCount);
        Assert.Equal(scenario.Latest.Id, Assert.Single(globalHistoryPage.Items).Id);
        Assert.True(globalHistoryPage.HasMore);

        var ascendingHistoryPage = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            sort: "recorded_asc");
        Assert.Equal(scenario.CatalogEntry.Id, ascendingHistoryPage.Items.First().Id);
        var lowestValueHistoryPage = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            sort: "value_asc");
        Assert.Equal(scenario.BlankEntry.Id, lowestValueHistoryPage.Items.First().Id);
        var defaultHistoryPage = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            sort: "unsupported");
        Assert.Equal(scenario.HondaMaintenanceEntry.Id, defaultHistoryPage.Items.First().Id);

        var filteredHistoryPage = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            vehicleId: scenario.Camry.Id,
            page: 1,
            pageSize: 1,
            searchQuery: "ТЕКУЩЕЕ",
            sort: "value_asc");
        Assert.Equal(scenario.Latest.Id, Assert.Single(filteredHistoryPage.Items).Id);
        Assert.False(filteredHistoryPage.HasMore);
        var clientHistoryPage = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            scenario.Organization.Id,
            organizationClientId: scenario.Client.Id);
        Assert.Equal(scenario.FleetEntry.Id, Assert.Single(clientHistoryPage.Items).Id);
        var clientSearchPage = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            scenario.Organization.Id,
            searchQuery: scenario.Client.Name);
        Assert.Equal(scenario.FleetEntry.Id, Assert.Single(clientSearchPage.Items).Id);
        var brandSearchPage = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            searchQuery: "Toyota");
        Assert.Equal(3, brandSearchPage.TotalCount);
        var modelSearchPage = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            searchQuery: scenario.CatalogModelName);
        Assert.Equal(scenario.CatalogEntry.Id, Assert.Single(modelSearchPage.Items).Id);
        var nameSearchPage = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            searchQuery: "Внутренний ярлык");
        Assert.Equal(3, nameSearchPage.TotalCount);
        var commentSearchPage = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            searchQuery: "Предыдущее значение");
        Assert.Equal(scenario.Earlier.Id, Assert.Single(commentSearchPage.Items).Id);
        var noMatchSearchPage = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            searchQuery: "неизвестное значение");
        Assert.Empty(noMatchSearchPage.Items);

        var timelineRecordedAsc = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            vehicleId: scenario.Camry.Id,
            page: 0,
            pageSize: 0,
            sort: "recorded_asc");
        Assert.Equal(1, timelineRecordedAsc.Page);
        Assert.Equal(12, timelineRecordedAsc.PageSize);
        Assert.Equal(scenario.Earlier.Id, timelineRecordedAsc.Items.First().Id);
        var timelineValueDesc = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            vehicleId: scenario.Camry.Id,
            sort: "value_desc");
        Assert.Equal(scenario.Latest.Id, timelineValueDesc.Items.First().Id);
        var timelineValueAsc = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            vehicleId: scenario.Camry.Id,
            sort: "value_asc");
        Assert.Equal(scenario.Earlier.Id, timelineValueAsc.Items.First().Id);
        var timelineDefault = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            vehicleId: scenario.Camry.Id,
            sort: "unsupported");
        Assert.Equal(scenario.Maintenance.Id, timelineDefault.Items.First().Id);
        var organizationTimeline = await scenario.Service.GetCounterTimelineAsync(
            scenario.User.Id,
            scenario.Organization.Id,
            scenario.FleetVehicle.Id);
        Assert.Equal(scenario.FleetEntry.Id, Assert.Single(organizationTimeline).Id);

        var timelineSearchDisplayName = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            vehicleId: scenario.Camry.Id,
            searchQuery: "Toyota");
        Assert.Equal(4, timelineSearchDisplayName.TotalCount);
        var timelineSearchComment = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            vehicleId: scenario.Camry.Id,
            searchQuery: "Текущее показание");
        Assert.Equal(scenario.Latest.Id, Assert.Single(timelineSearchComment.Items).Id);
        var timelineSearchSubtitle = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            vehicleId: scenario.Camry.Id,
            searchQuery: "2020");
        Assert.Equal(4, timelineSearchSubtitle.TotalCount);
        var timelineSearchEmpty = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            vehicleId: scenario.BlankVehicle.Id,
            searchQuery: "no-match");
        Assert.Empty(timelineSearchEmpty.Items);
        Assert.Equal(0, timelineSearchEmpty.AllCount);

        var organizationTimelineSearch = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            scenario.Organization.Id,
            vehicleId: scenario.FleetVehicle.Id,
            organizationClientId: scenario.Client.Id,
            searchQuery: scenario.Client.Name);
        Assert.Equal(scenario.FleetEntry.Id, Assert.Single(organizationTimelineSearch.Items).Id);
        var timelineWithDifferentClient = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            scenario.Organization.Id,
            vehicleId: scenario.FleetVehicle.Id,
            organizationClientId: Guid.NewGuid());
        Assert.Empty(timelineWithDifferentClient.Items);
        var personalTimelineWithClient = await scenario.Service.GetCounterHistoryPageAsync(
            scenario.User.Id,
            null,
            vehicleId: scenario.Camry.Id,
            organizationClientId: scenario.Client.Id);
        Assert.Empty(personalTimelineWithClient.Items);
    }

    [Fact]
    public async Task CounterDateLookupReturnsManualMaintenanceAndEmptySnapshots()
    {
        await using var context = fixture.CreateContext();
        var scenario = await SeedCounterScenarioAsync(context);

        var dateLookup = await scenario.Service.GetCounterEntryForDateAsync(
            scenario.User.Id,
            null,
            scenario.Camry.Id,
            scenario.RecordedAt.AddHours(8));
        Assert.True(dateLookup.HasEntry);
        Assert.Equal(scenario.Latest.Id, dateLookup.EntryId);
        Assert.Equal(scenario.RecordedAt.Date, dateLookup.RecordedAt);
        Assert.Equal("Текущее показание", dateLookup.Comment);
        Assert.Contains("2020 г.", dateLookup.VehicleSubtitle);

        var missingLookup = await scenario.Service.GetCounterEntryForDateAsync(
            scenario.User.Id,
            null,
            scenario.Camry.Id,
            scenario.RecordedAt.AddDays(4));
        Assert.False(missingLookup.HasEntry);
        Assert.Null(missingLookup.EntryId);
        Assert.Equal(VehicleCounterSourceTypes.Manual, missingLookup.SourceType);

        var maintenanceLookup = await scenario.Service.GetCounterEntryForDateAsync(
            scenario.User.Id,
            null,
            scenario.Honda.Id,
            scenario.RecordedAt.AddDays(3));
        Assert.True(maintenanceLookup.HasEntry);
        Assert.Equal(scenario.HondaMaintenanceEntry.Id, maintenanceLookup.EntryId);
        Assert.Equal(VehicleCounterSourceTypes.Maintenance, maintenanceLookup.SourceType);

        var emptyVehicleLookup = await scenario.Service.GetCounterEntryForDateAsync(
            scenario.User.Id,
            null,
            scenario.BlankVehicle.Id,
            scenario.RecordedAt);
        Assert.Null(emptyVehicleLookup.VehicleSubtitle);
        Assert.Equal(string.Empty, emptyVehicleLookup.SourceName);

        var organizationLookup = await scenario.Service.GetCounterEntryForDateAsync(
            scenario.User.Id,
            scenario.Organization.Id,
            scenario.FleetVehicle.Id,
            scenario.RecordedAt);
        Assert.Equal(scenario.Client.Name, organizationLookup.OrganizationClientName);
        Assert.Contains(scenario.Client.Name, organizationLookup.VehicleSubtitle);

        await using var freshContext = fixture.CreateContext();
        var freshService = new VehicleCounterQueryService(
            new VehicleRepository(freshContext),
            freshContext,
            scenario.Access.Object);
        var catalogLookup = await freshService.GetCounterEntryForDateAsync(
            scenario.User.Id,
            null,
            scenario.CatalogVehicle.Id,
            scenario.RecordedAt.AddDays(-3));
        Assert.False(catalogLookup.HasEntry);
        Assert.Equal($"{scenario.CatalogBrandName} {scenario.CatalogModelName}", catalogLookup.VehicleDisplayName);
        var freshOrganizationLookup = await freshService.GetCounterEntryForDateAsync(
            scenario.User.Id,
            scenario.Organization.Id,
            scenario.FleetVehicle.Id,
            scenario.RecordedAt);
        Assert.Equal(scenario.Client.Name, freshOrganizationLookup.OrganizationClientName);
        Assert.Equal("Ural Next", freshOrganizationLookup.VehicleDisplayName);
        Assert.Contains(scenario.Client.Name, freshOrganizationLookup.VehicleSubtitle);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            freshService.GetCounterEntryForDateAsync(scenario.User.Id, null, Guid.NewGuid(), scenario.RecordedAt));
        scenario.Access.Verify(service => service.EnsureWorkspaceCanReadAsync(
            scenario.User.Id,
            scenario.Organization.Id,
            OrganizationPermissionCodes.CountersView), Times.AtLeastOnce);
    }

    private async Task<CounterScenario> SeedCounterScenarioAsync(OpenCRMContext context)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = $"counters-{Guid.NewGuid():N}@example.test" };
        var otherUser = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = $"other-counters-{Guid.NewGuid():N}@example.test" };
        var organization = new Organization { Name = "Парк", OwnerUser = user, OwnerUserId = user.Id };
        var client = new OrganizationClient { Organization = organization, OrganizationId = organization.Id, Name = "Клиент парка" };
        var catalogSuffix = Guid.NewGuid().ToString("N")[..8];
        var catalogBrand = new Brand { Name = $"Acme {catalogSuffix}", Kind = VehicleKind.Truck };
        var catalogModel = new Model { Name = $"Hauler {catalogSuffix}", Brand = catalogBrand, Kind = VehicleKind.Truck };
        context.Users.AddRange(user, otherUser);
        context.Organizations.Add(organization);
        context.OrganizationClients.Add(client);
        context.Models.Add(catalogModel);
        await context.SaveChangesAsync();

        var camry = CreateVehicle(user.Id, "Toyota", "Camry", "C001CC", DateTime.UtcNow.AddDays(-2), 120_000, DateTime.UtcNow.AddDays(-1));
        camry.YearIssue = 2020;
        camry.Name = "Внутренний ярлык";
        var honda = CreateVehicle(user.Id, "Honda", "Civic", "C002CC", DateTime.UtcNow.AddDays(-1), null, null);
        var catalogVehicle = CreateVehicle(user.Id, "", "", "C007CC", DateTime.UtcNow.AddDays(-4), null, null);
        catalogVehicle.BrandId = catalogBrand.Id;
        catalogVehicle.Brand = catalogBrand;
        catalogVehicle.ModelId = catalogModel.Id;
        catalogVehicle.Model = catalogModel;
        catalogVehicle.ManualBrandName = null;
        catalogVehicle.ManualModelName = null;
        var blankVehicle = CreateVehicle(user.Id, "", "", "", DateTime.UtcNow.AddDays(-3), null, null);
        blankVehicle.ManualBrandName = null;
        blankVehicle.ManualModelName = null;
        blankVehicle.Name = null;
        blankVehicle.RegistrationPlate = null;
        var archived = CreateVehicle(user.Id, "Lada", "Niva", "C003CC", DateTime.UtcNow, 80_000, DateTime.UtcNow);
        archived.IsArchived = true;
        var deleted = CreateVehicle(user.Id, "Ford", "Focus", "C004CC", DateTime.UtcNow, 90_000, DateTime.UtcNow);
        deleted.IsDeleted = true;
        var foreign = CreateVehicle(otherUser.Id, "BMW", "X5", "C005CC", DateTime.UtcNow, 150_000, DateTime.UtcNow);
        var fleetVehicle = CreateVehicle(user.Id, "Ural", "Next", "C006CC", DateTime.UtcNow, 40_000, DateTime.UtcNow);
        fleetVehicle.OrganizationId = organization.Id;
        fleetVehicle.Organization = organization;
        fleetVehicle.OrganizationClientId = client.Id;
        fleetVehicle.OrganizationClient = client;
        context.Vehicles.AddRange(camry, honda, catalogVehicle, blankVehicle, archived, deleted, foreign, fleetVehicle);

        var recordedAt = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        var earlier = new VehicleCounterEntry
        {
            VehicleId = camry.Id,
            Vehicle = camry,
            UserId = user.Id,
            User = user,
            Value = 110_000,
            RecordedAt = recordedAt.AddDays(-1),
            Comment = "Предыдущее значение",
            SourceType = VehicleCounterSourceTypes.Manual
        };
        var latest = new VehicleCounterEntry
        {
            VehicleId = camry.Id,
            Vehicle = camry,
            UserId = user.Id,
            User = user,
            Value = 120_000,
            RecordedAt = recordedAt,
            Comment = "Текущее показание",
            SourceType = VehicleCounterSourceTypes.Manual
        };
        var deletedEntry = new VehicleCounterEntry
        {
            VehicleId = camry.Id,
            Vehicle = camry,
            UserId = user.Id,
            User = user,
            Value = 130_000,
            RecordedAt = recordedAt.AddDays(1),
            Comment = "Удаленное показание",
            SourceType = VehicleCounterSourceTypes.Manual,
            IsDeleted = true
        };
        var fleetEntry = new VehicleCounterEntry
        {
            VehicleId = fleetVehicle.Id,
            Vehicle = fleetVehicle,
            UserId = user.Id,
            User = user,
            Value = 40_000,
            RecordedAt = recordedAt,
            Comment = "Парк",
            SourceType = VehicleCounterSourceTypes.Manual
        };
        var blankEntry = new VehicleCounterEntry
        {
            VehicleId = blankVehicle.Id,
            Vehicle = blankVehicle,
            UserId = user.Id,
            User = user,
            Value = 10,
            RecordedAt = recordedAt.AddDays(-2),
            Comment = "Без названия",
            SourceType = VehicleCounterSourceTypes.Manual
        };
        var catalogEntry = new VehicleCounterEntry
        {
            VehicleId = catalogVehicle.Id,
            Vehicle = catalogVehicle,
            UserId = user.Id,
            User = user,
            Value = 20,
            RecordedAt = recordedAt.AddDays(-4),
            Comment = "Показание каталога",
            SourceType = VehicleCounterSourceTypes.Manual
        };
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            UserId = user.Id,
            User = user,
            VehicleId = camry.Id,
            Vehicle = camry,
            Type = MaintenanceType.Repair,
            Date = recordedAt.AddHours(1),
            DateTimeCreation = DateTime.UtcNow,
            Mileage = 115_000,
            Comment = "Показание при обслуживании"
        };
        context.Maintenance.Add(maintenance);
        var maintenanceEntry = new VehicleCounterEntry
        {
            VehicleId = camry.Id,
            Vehicle = camry,
            UserId = user.Id,
            User = user,
            MaintenanceId = maintenance.Id,
            Maintenance = maintenance,
            Value = 115_000,
            RecordedAt = recordedAt.AddMinutes(2),
            Comment = "Синхронизировано из обслуживания",
            SourceType = VehicleCounterSourceTypes.Maintenance
        };
        var hondaMaintenance = new OpenCRM.Domain.Maintenance
        {
            UserId = user.Id,
            User = user,
            VehicleId = honda.Id,
            Vehicle = honda,
            Type = MaintenanceType.Repair,
            Date = recordedAt.AddDays(3),
            DateTimeCreation = DateTime.UtcNow,
            Mileage = 500,
            Comment = "Обслуживание Honda"
        };
        context.Maintenance.Add(hondaMaintenance);
        var hondaMaintenanceEntry = new VehicleCounterEntry
        {
            VehicleId = honda.Id,
            Vehicle = honda,
            UserId = user.Id,
            User = user,
            MaintenanceId = hondaMaintenance.Id,
            Maintenance = hondaMaintenance,
            Value = 500,
            RecordedAt = recordedAt.AddDays(3),
            Comment = "Синхронизировано из обслуживания",
            SourceType = VehicleCounterSourceTypes.Maintenance
        };
        context.VehicleCounterEntries.AddRange(earlier, latest, deletedEntry, fleetEntry, blankEntry, catalogEntry, maintenanceEntry, hondaMaintenanceEntry);
        await context.SaveChangesAsync();

        var access = new Mock<IOrganizationSubscriptionService>();
        access.Setup(service => service.EnsureWorkspaceCanReadAsync(user.Id, organization.Id, It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        var service = new VehicleCounterQueryService(new VehicleRepository(context), context, access.Object);

        return new CounterScenario(
            user,
            organization,
            client,
            catalogBrand.Name,
            catalogModel.Name,
            camry,
            honda,
            catalogVehicle,
            blankVehicle,
            archived,
            deleted,
            foreign,
            fleetVehicle,
            earlier,
            latest,
            fleetEntry,
            blankEntry,
            catalogEntry,
            maintenance,
            maintenanceEntry,
            hondaMaintenanceEntry,
            recordedAt,
            service,
            access);
    }

    private sealed record CounterScenario(
        ApplicationUser User,
        Organization Organization,
        OrganizationClient Client,
        string CatalogBrandName,
        string CatalogModelName,
        Vehicle Camry,
        Vehicle Honda,
        Vehicle CatalogVehicle,
        Vehicle BlankVehicle,
        Vehicle Archived,
        Vehicle Deleted,
        Vehicle Foreign,
        Vehicle FleetVehicle,
        VehicleCounterEntry Earlier,
        VehicleCounterEntry Latest,
        VehicleCounterEntry FleetEntry,
        VehicleCounterEntry BlankEntry,
        VehicleCounterEntry CatalogEntry,
        OpenCRM.Domain.Maintenance Maintenance,
        VehicleCounterEntry MaintenanceEntry,
        VehicleCounterEntry HondaMaintenanceEntry,
        DateTime RecordedAt,
        VehicleCounterQueryService Service,
        Mock<IOrganizationSubscriptionService> Access);

    private static Vehicle CreateVehicle(
        string userId,
        string brand,
        string model,
        string plate,
        DateTime createdAt,
        int? currentValue,
        DateTime? updatedAt)
    {
        return new Vehicle
        {
            UserId = userId,
            ManualBrandName = brand,
            ManualModelName = model,
            Name = $"{brand} {model}",
            RegistrationPlate = plate,
            DateTimeCreation = createdAt,
            CurrentMileage = currentValue,
            CurrentMileageUpdatedAt = updatedAt
        };
    }
}
