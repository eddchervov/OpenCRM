using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

[Collection(CatalogDatabaseCollection.Name)]
public sealed class CatalogQueryTests(CatalogDatabaseFixture fixture)
{
    [Fact]
    public async Task QueryReturnsOnlyOrganizationItemsIncludingArchivedAndActiveServiceLookupIsScoped()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<ICatalogQueryService>();
        var catalog = await queries.GetAsync(organization.OwnerUserId, organization.Id);
        var foreignCatalog = await queries.GetAsync(foreignOrganization.OwnerUserId, foreignOrganization.Id);
        var activeService = catalog.Services.First();
        var selectedServiceToRead = catalog.Services.Skip(1).First();
        var foreignService = foreignCatalog.Services.First();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();

        await command.DeleteAsync(organization.OwnerUserId, organization.Id, activeService.Id);
        var archivedCatalog = await queries.GetAsync(organization.OwnerUserId, organization.Id);
        var archivedService = archivedCatalog.Services.Single(item => item.Id == activeService.Id);
        var selectedService = await queries.GetActiveServiceAsync(organization.OwnerUserId, organization.Id, selectedServiceToRead.Id);
        var foreignException = await Assert.ThrowsAsync<InvalidOperationException>(() => queries.GetActiveServiceAsync(
            organization.OwnerUserId, organization.Id, foreignService.Id));

        Assert.Equal(organization.Id, catalog.OrganizationId);
        Assert.Equal(OrganizationTypeCodes.Detailing, catalog.ScenarioCode);
        Assert.All(catalog.Services, item => Assert.Equal(catalog.ScenarioCode, item.ScenarioCode));
        Assert.DoesNotContain(catalog.Services, item => item.Id == foreignService.Id);
        Assert.False(archivedService.IsActive);
        Assert.Equal(selectedServiceToRead.Id, selectedService.Id);
        Assert.Equal("Выберите действующую услугу из справочника этой организации.", foreignException.Message);
    }

    [Fact]
    public async Task QueryDoesNotSaveUncommittedChangesFromCallerContext()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var persistedName = organization.Name;
        organization.Name = "Измененное без сохранения";
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<ICatalogQueryService>();

        var result = await query.GetAsync(organization.OwnerUserId, organization.Id);

        await using var verificationContext = fixture.CreateContext();
        var savedOrganization = await verificationContext.Organizations.AsNoTracking()
            .SingleAsync(value => value.Id == organization.Id);
        Assert.Equal(persistedName, savedOrganization.Name);
        Assert.Equal(organization.Id, result.OrganizationId);
    }

    [Fact]
    public async Task QueryReturnsCountAndFirstTwentyOpenAssignmentsForUsedPost()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        var postName = $"Пост {Guid.NewGuid():N}";
        var post = CatalogQueryTests.CreateWorkPost(organization.Id, organization.PrimaryScenarioCode, postName);
        var vehicle = new Vehicle { User = organization.OwnerUser, Organization = organization };
        var startDate = DateTime.UtcNow.Date;
        context.OrganizationScenarioCatalogItems.Add(post);
        for (var index = 0; index < 22; index++)
        {
            context.Maintenance.Add(new OpenCRM.Domain.Maintenance
            {
                User = organization.OwnerUser,
                Vehicle = vehicle,
                Location = index == 0 ? $" {postName.ToUpperInvariant()} " : postName,
                Date = startDate.AddDays(index),
                PlannedDate = startDate.AddDays(index),
                Status = index % 2 == 0 ? MaintenanceStatus.Planned : MaintenanceStatus.InProgress,
                Details = []
            });
        }

        context.Maintenance.AddRange(
            new OpenCRM.Domain.Maintenance
            {
                User = organization.OwnerUser,
                Vehicle = vehicle,
                Location = postName,
                Date = DateTime.UtcNow,
                Status = MaintenanceStatus.Completed,
                Details = []
            },
            new OpenCRM.Domain.Maintenance
            {
                User = organization.OwnerUser,
                Vehicle = vehicle,
                Location = postName,
                Date = DateTime.UtcNow,
                Status = MaintenanceStatus.Cancelled,
                Details = []
            },
            new OpenCRM.Domain.Maintenance
            {
                User = foreignOrganization.OwnerUser,
                Vehicle = new Vehicle { User = foreignOrganization.OwnerUser, Organization = foreignOrganization },
                Location = postName,
                Date = DateTime.UtcNow,
                Status = MaintenanceStatus.Planned,
                Details = []
            });
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<ICatalogQueryService>();

        var result = await queries.GetAffectedWorkPostMaintenancesAsync(organization.OwnerUserId, organization.Id, post.Id);

        Assert.Equal(22, result.TotalCount);
        Assert.Equal(20, result.Items.Count);
        Assert.Equal(Enumerable.Range(0, 20).Select(index => startDate.AddDays(index)),
            result.Items.Select(item => item.Date.Date));
        Assert.All(result.Items, item => Assert.True(item.Status == (int)MaintenanceStatus.Planned
            || item.Status == (int)MaintenanceStatus.InProgress));
    }

    [Fact]
    public async Task QueryAffectedWorkPostMaintenancesRequiresMaintenanceViewPermission()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var post = CatalogQueryTests.CreateWorkPost(organization.Id, organization.PrimaryScenarioCode, "Пост истории");
        context.OrganizationScenarioCatalogItems.Add(post);
        await context.SaveChangesAsync();
        var permissions = new Mock<IOrganizationSubscriptionService>();
        permissions.Setup(service => service.EnsureWorkspaceCanReadAsync(
                organization.OwnerUserId,
                organization.Id,
                OrganizationPermissionCodes.MaintenanceView))
            .ThrowsAsync(new InvalidOperationException("Нет права просматривать обслуживания."));
        using var services = fixture.CreateServices(context, permissions.Object);
        using var scope = services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<ICatalogQueryService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queries.GetAffectedWorkPostMaintenancesAsync(organization.OwnerUserId, organization.Id, post.Id));

        Assert.Equal("Нет права просматривать обслуживания.", exception.Message);
        permissions.Verify(service => service.EnsureWorkspaceCanReadAsync(
            organization.OwnerUserId,
            organization.Id,
            OrganizationPermissionCodes.MaintenanceView), Times.Once);
    }

    [Fact]
    public async Task ActiveServiceQueryRequiresActiveServiceOwnedByOrganization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<ICatalogQueryService>();
        var initialization = scope.ServiceProvider.GetRequiredService<ICatalogInitializationService>();
        await initialization.EnsureDefaultsAsync(organization.Id);
        await initialization.EnsureDefaultsAsync(foreignOrganization.Id);
        var serviceIds = await context.OrganizationScenarioCatalogItems
            .Where(item => item.OrganizationId == organization.Id && item.KindCode == OrganizationScenarioCatalogKindCodes.Service)
            .Select(item => item.Id)
            .ToArrayAsync();
        var item = await queries.GetActiveServiceAsync(organization.OwnerUserId, organization.Id, serviceIds[0]);
        var inactive = await context.OrganizationScenarioCatalogItems.SingleAsync(value => value.Id == serviceIds[1]);
        inactive.IsActive = false;
        await context.SaveChangesAsync();

        var inactiveException = await Assert.ThrowsAsync<InvalidOperationException>(() => queries.GetActiveServiceAsync(
            organization.OwnerUserId, organization.Id, inactive.Id));
        var foreignServiceId = await context.OrganizationScenarioCatalogItems.Where(value => value.OrganizationId == foreignOrganization.Id
            && value.KindCode == OrganizationScenarioCatalogKindCodes.Service).Select(value => value.Id).FirstAsync();
        var materialId = await context.OrganizationScenarioCatalogItems.Where(value => value.OrganizationId == organization.Id
            && value.KindCode == OrganizationScenarioCatalogKindCodes.Material).Select(value => value.Id).FirstAsync();
        var foreignException = await Assert.ThrowsAsync<InvalidOperationException>(() => queries.GetActiveServiceAsync(
            organization.OwnerUserId, organization.Id, foreignServiceId));
        var notServiceException = await Assert.ThrowsAsync<InvalidOperationException>(() => queries.GetActiveServiceAsync(
            organization.OwnerUserId, organization.Id, materialId));

        Assert.Equal(serviceIds[0], item.Id);
        Assert.Equal("Выберите действующую услугу из справочника этой организации.", inactiveException.Message);
        Assert.Equal(inactiveException.Message, foreignException.Message);
        Assert.Equal(inactiveException.Message, notServiceException.Message);
    }

    [Fact]
    public async Task QueryMarksUsageFromBundlesStockMovementsMaintenanceRowsAndTrimmedPostLocation()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();
        var query = scope.ServiceProvider.GetRequiredService<ICatalogQueryService>();
        await scope.ServiceProvider.GetRequiredService<ICatalogInitializationService>().EnsureDefaultsAsync(organization.Id);

        var serviceItems = await context.OrganizationScenarioCatalogItems
            .Where(item => item.OrganizationId == organization.Id && item.KindCode == OrganizationScenarioCatalogKindCodes.Service)
            .OrderBy(item => item.SortOrder)
            .ToArrayAsync();
        var materials = await context.OrganizationScenarioCatalogItems
            .Where(item => item.OrganizationId == organization.Id && item.KindCode == OrganizationScenarioCatalogKindCodes.Material)
            .OrderBy(item => item.SortOrder)
            .ToArrayAsync();
        var workPost = await context.OrganizationScenarioCatalogItems.SingleAsync(item =>
            item.OrganizationId == organization.Id
            && item.KindCode == OrganizationScenarioCatalogKindCodes.WorkPost
            && item.Name == "Пост 1");

        await command.CreateAsync(organization.OwnerUserId, organization.Id, new OpenCRM.Modules.Catalog.Contracts.Dtos.CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.ServiceBundle,
            Name = "Набор использования",
            ServiceIds = [serviceItems[0].Id]
        });

        var stockOnly = new OrganizationMaterialStock
        {
            OrganizationId = organization.Id,
            CatalogItemId = materials[0].Id,
            Name = "Материал склад",
            Unit = "шт"
        };
        var movementOnlyStock = new OrganizationMaterialStock
        {
            OrganizationId = organization.Id,
            Name = "Ручной остаток",
            Unit = "л"
        };
        context.OrganizationMaterialStocks.AddRange(stockOnly, movementOnlyStock);
        await context.SaveChangesAsync();
        context.OrganizationMaterialMovements.Add(new OrganizationMaterialMovement
        {
            OrganizationId = organization.Id,
            StockId = movementOnlyStock.Id,
            CatalogItemId = materials[1].Id,
            TypeCode = OrganizationMaterialMovementTypeCodes.Receipt,
            Quantity = 1,
            Unit = "л"
        });

        var vehicle = new Vehicle
        {
            OrganizationId = organization.Id,
            Organization = organization,
            UserId = organization.OwnerUserId,
            User = organization.OwnerUser,
            DateTimeCreation = DateTime.UtcNow
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            Date = DateTime.UtcNow,
            DateTimeCreation = DateTime.UtcNow,
            UserId = organization.OwnerUserId,
            User = organization.OwnerUser,
            VehicleId = vehicle.Id,
            Vehicle = vehicle,
            Location = "  ПОСТ 1  ",
            Type = MaintenanceType.Maintenance,
            Details =
            [
                new MaintenanceDetail { Name = serviceItems[2].Name, CatalogItemId = serviceItems[2].Id },
                new MaintenanceDetail { Name = "Расход материала", MaterialCatalogItemId = materials[2].Id }
            ]
        };
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();

        var result = await query.GetAsync(organization.OwnerUserId, organization.Id);
        Assert.True(result.Services.Single(item => item.Id == serviceItems[0].Id).IsUsed);
        Assert.True(result.Services.Single(item => item.Id == serviceItems[2].Id).IsUsed);
        Assert.True(result.Materials.Single(item => item.Id == materials[0].Id).IsUsed);
        Assert.True(result.Materials.Single(item => item.Id == materials[1].Id).IsUsed);
        Assert.True(result.Materials.Single(item => item.Id == materials[2].Id).IsUsed);
        Assert.True(result.WorkPosts.Single(item => item.Id == workPost.Id).IsUsed);
        Assert.False(result.Services.Single(item => item.Id == serviceItems[4].Id).IsUsed);

        maintenance.IsDeleted = true;
        materials[2].IsActive = false;
        await context.SaveChangesAsync();

        var historical = await query.GetAsync(organization.OwnerUserId, organization.Id);
        Assert.True(historical.Services.Single(item => item.Id == serviceItems[2].Id).IsUsed);
        Assert.True(historical.Materials.Single(item => item.Id == materials[2].Id).IsUsed);
        Assert.False(historical.Materials.Single(item => item.Id == materials[2].Id).IsActive);
        Assert.True(historical.WorkPosts.Single(item => item.Id == workPost.Id).IsUsed);

        await command.DeleteAsync(organization.OwnerUserId, organization.Id, workPost.Id);
        var usedMaterialException = await Assert.ThrowsAsync<InvalidOperationException>(() => command.DeleteAsync(
            organization.OwnerUserId, organization.Id, materials[2].Id));
        Assert.False((await context.OrganizationScenarioCatalogItems.AsNoTracking()
            .SingleAsync(item => item.Id == workPost.Id)).IsActive);
        Assert.Equal("Элемент справочника уже используется. Его нельзя удалить.", usedMaterialException.Message);
    }

    [Fact]
    public async Task ReadPermissionFailureDoesNotInitializeCatalog()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(service => service.EnsureWorkspaceCanReadAsync(
                organization.OwnerUserId,
                organization.Id,
                OrganizationPermissionCodes.WorkspaceView))
            .ThrowsAsync(new InvalidOperationException("Нет доступа к справочнику."));
        using var services = fixture.CreateServices(context, subscription.Object);
        using var scope = services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<ICatalogQueryService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => query.GetAsync(organization.OwnerUserId, organization.Id));

        Assert.Equal("Нет доступа к справочнику.", exception.Message);
        Assert.False(await context.OrganizationScenarioCatalogItems.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task QueryThrowsForMissingOrganizationAfterInitializationIsSkipped()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<ICatalogQueryService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => query.GetAsync(
            organization.OwnerUserId, Guid.NewGuid()));

        Assert.Equal("Организация не найдена.", exception.Message);
        Assert.False(await context.OrganizationScenarioCatalogItems.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task ActiveServicePermissionFailureIsPropagatedBeforeLookup()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                organization.OwnerUserId,
                organization.Id,
                OrganizationPermissionCodes.MaintenanceCreate))
            .ThrowsAsync(new InvalidOperationException("Нет права выбирать услугу."));
        using var services = fixture.CreateServices(context, subscription.Object);
        using var scope = services.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<ICatalogQueryService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => query.GetActiveServiceAsync(
            organization.OwnerUserId, organization.Id, Guid.NewGuid()));

        Assert.Equal("Нет права выбирать услугу.", exception.Message);
        Assert.False(await context.OrganizationScenarioCatalogItems.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    private static OrganizationScenarioCatalogItem CreateWorkPost(Guid organizationId, string scenarioCode, string name) => new()
    {
        OrganizationId = organizationId,
        ScenarioCode = scenarioCode,
        KindCode = OrganizationScenarioCatalogKindCodes.WorkPost,
        Code = $"post-{Guid.NewGuid():N}",
        Name = name,
        IsActive = true
    };
}
