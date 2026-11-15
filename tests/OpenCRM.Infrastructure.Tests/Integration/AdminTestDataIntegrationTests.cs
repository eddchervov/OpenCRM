using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Admin;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Testing;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class AdminTestDataIntegrationTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Fact]
    public async Task OrganizationSearchReturnsFilteredStablePagesAndSelectedOrganization()
    {
        await using var context = fixture.CreateContext();
        var organizations = Enumerable.Range(1, 33)
            .Select(index =>
            {
                var owner = new ApplicationUser
                {
                    Email = $"paging-owner-{index}@example.test",
                    UserName = $"paging-owner-{index}@example.test"
                };
                return new Organization
                {
                    Id = Guid.Parse($"00000000-0000-0000-0000-{index:D12}"),
                    Name = index <= 2 ? "Paging Integration Duplicate" : $"Paging Integration {index:D2}",
                    OwnerUser = owner,
                    PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation
                };
            })
            .ToArray();
        var deletedOrganization = new Organization
        {
            Name = "Paging Integration Deleted",
            OwnerUser = new ApplicationUser { Email = "paging-deleted@example.test", UserName = "paging-deleted" },
            IsDeleted = true
        };
        context.Organizations.AddRange(organizations);
        context.Organizations.Add(deletedOrganization);
        await context.SaveChangesAsync();
        var service = new AdminTestDataService(context);

        var firstPage = await service.SearchOrganizationsPageAsync("Paging Integration", 1, 10, default);
        var secondPage = await service.SearchOrganizationsPageAsync("Paging Integration", 2, 10, default);
        var thirdPage = await service.SearchOrganizationsPageAsync("Paging Integration", 3, 10, default);
        var lastPage = await service.SearchOrganizationsPageAsync("Paging Integration", 100, 10, default);
        var minimumPageSize = await service.SearchOrganizationsPageAsync("Paging Integration", 0, 1, default);
        var maximumPageSize = await service.SearchOrganizationsPageAsync("Paging Integration", 0, 500, default);
        var ownerSearch = await service.SearchOrganizationsPageAsync("paging-owner-7@example.test", 1, 20, default);
        var selectedOrganization = await service.GetOrganizationAsync(organizations[6].Id, default);
        var deletedSelection = await service.GetOrganizationAsync(deletedOrganization.Id, default);

        var expectedOrder = organizations.OrderBy(item => item.Name).ThenBy(item => item.Id).Select(item => item.Id);
        Assert.Equal(33, firstPage.TotalCount);
        Assert.Equal(10, firstPage.Items.Count);
        Assert.Equal(2, secondPage.Page);
        Assert.Equal(10, secondPage.Items.Count);
        Assert.Equal(3, thirdPage.Page);
        Assert.Equal(10, thirdPage.Items.Count);
        Assert.Equal(4, lastPage.Page);
        Assert.Equal(3, lastPage.Items.Count);
        Assert.Equal(expectedOrder, firstPage.Items.Concat(secondPage.Items).Concat(thirdPage.Items).Concat(lastPage.Items).Select(item => item.Id));
        Assert.Equal(10, minimumPageSize.PageSize);
        Assert.Equal(1, minimumPageSize.Page);
        Assert.Equal(100, maximumPageSize.PageSize);
        Assert.Equal(1, maximumPageSize.Page);
        Assert.Equal(1, ownerSearch.TotalCount);
        Assert.Equal(organizations[6].Id, Assert.Single(ownerSearch.Items).Id);
        Assert.Equal(organizations[6].Id, selectedOrganization?.Id);
        Assert.Null(deletedSelection);
    }

    [Fact]
    public async Task TestDataGenerationUsesSelectedOrganizationAndExpectedMaintenanceStates()
    {
        await using var context = fixture.CreateContext();
        await new AdminVehicleCatalogSeedService(context).SeedAsync();
        var owner = new ApplicationUser();
        var otherOwner = new ApplicationUser();
        var organization = new Organization
        {
            Name = "Организация генерации тестовых данных",
            OwnerUser = owner,
            PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation
        };
        var otherOrganization = new Organization
        {
            Name = "Другая организация генерации тестовых данных",
            OwnerUser = otherOwner,
            PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation
        };
        context.Organizations.AddRange(organization, otherOrganization);
        await context.SaveChangesAsync();
        var service = new AdminTestDataService(context);

        var generatedVehicles = await service.GenerateVehiclesAsync(organization.Id, 5);

        Assert.True(generatedVehicles.Succeeded);
        Assert.Equal(5, generatedVehicles.CreatedCount);
        var generatedVehicleIds = await context.Vehicles
            .Where(vehicle => vehicle.OrganizationId == organization.Id)
            .Select(vehicle => vehicle.Id)
            .ToListAsync();
        Assert.Equal(5, generatedVehicleIds.Count);
        Assert.All(await context.Vehicles.Where(vehicle => vehicle.OrganizationId == organization.Id).ToListAsync(), vehicle =>
        {
            Assert.Equal(owner.Id, vehicle.UserId);
            Assert.Equal(organization.Id, vehicle.OrganizationId);
        });
        Assert.Empty(await context.Vehicles.Where(vehicle => vehicle.OrganizationId == otherOrganization.Id).ToListAsync());

        var vehicleId = generatedVehicleIds[0];
        var plannedResult = await service.GenerateMaintenancesAsync(organization.Id, vehicleId, 3, "planned");
        var completedResult = await service.GenerateMaintenancesAsync(organization.Id, vehicleId, 3, "completed");

        Assert.True(plannedResult.Succeeded);
        Assert.Equal(3, plannedResult.CreatedCount);
        Assert.True(completedResult.Succeeded);
        Assert.Equal(3, completedResult.CreatedCount);
        var maintenances = await context.Maintenance
            .Include(maintenance => maintenance.Details)
            .Where(maintenance => maintenance.VehicleId == vehicleId)
            .ToListAsync();
        var planned = maintenances.Where(maintenance => maintenance.Status == MaintenanceStatus.Planned).ToArray();
        var completed = maintenances.Where(maintenance => maintenance.Status == MaintenanceStatus.Completed).ToArray();
        Assert.Equal(3, planned.Length);
        Assert.Equal(3, completed.Length);
        Assert.All(planned, maintenance =>
        {
            Assert.NotNull(maintenance.PlannedDate);
            Assert.Null(maintenance.CompletedDate);
        });
        Assert.All(completed, maintenance =>
        {
            Assert.Null(maintenance.PlannedDate);
            Assert.NotNull(maintenance.CompletedDate);
        });
        Assert.All(maintenances, maintenance =>
        {
            Assert.NotEmpty(maintenance.Details);
            Assert.Equal(
                Enumerable.Range(0, maintenance.Details.Count),
                maintenance.Details.Select(detail => detail.Position));
        });
    }

    [Fact]
    public async Task TestDataGenerationClampsVehicleAndMaintenanceCountsToAdminLimits()
    {
        await using var context = fixture.CreateContext();
        await new AdminVehicleCatalogSeedService(context).SeedAsync();
        var owner = new ApplicationUser();
        var organization = new Organization
        {
            Name = "Организация проверки лимитов тестовых данных",
            OwnerUser = owner,
            PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var service = new AdminTestDataService(context);

        var vehiclesResult = await service.GenerateVehiclesAsync(organization.Id, 101);

        Assert.True(vehiclesResult.Succeeded);
        Assert.Equal(100, vehiclesResult.CreatedCount);
        Assert.Equal(100, await context.Vehicles.CountAsync(vehicle => vehicle.OrganizationId == organization.Id));
        var vehicle = await context.Vehicles.FirstAsync(item => item.OrganizationId == organization.Id);

        var maintenancesResult = await service.GenerateMaintenancesAsync(organization.Id, vehicle.Id, 301, "planned");

        Assert.True(maintenancesResult.Succeeded);
        Assert.Equal(300, maintenancesResult.CreatedCount);
        Assert.Equal(300, await context.Maintenance.CountAsync(maintenance => maintenance.VehicleId == vehicle.Id));
    }
}
