using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

[Collection(CatalogDatabaseCollection.Name)]
public sealed class CatalogArchivedSelectionTests(CatalogDatabaseFixture fixture)
{
    [Fact]
    public async Task BundleWithDurationKeepsOrderedServicesAndOwnDuration()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var commands = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();
        var service = await commands.CreateAsync(organization.OwnerUserId, organization.Id,
            new CreateOrganizationScenarioCatalogItemDto
            {
                KindCode = OrganizationScenarioCatalogKindCodes.Service,
                Name = "Полировка",
                DurationMinutes = 60
            });
        var bundle = await commands.CreateAsync(organization.OwnerUserId, organization.Id,
            new CreateOrganizationScenarioCatalogItemDto
            {
                KindCode = OrganizationScenarioCatalogKindCodes.ServiceBundle,
                Name = "Полировка и осмотр",
                ServiceIds = [service.Id],
                DurationMinutes = 90
            });

        Assert.Equal(90, bundle.DurationMinutes);
        Assert.Equal(new[] { service.Id }, bundle.ServiceIds);
        var saved = await scope.ServiceProvider.GetRequiredService<ICatalogQueryService>()
            .GetAsync(organization.OwnerUserId, organization.Id);
        Assert.Equal(90, saved.ServiceBundles.Single(item => item.Id == bundle.Id).DurationMinutes);
        Assert.Equal(60, saved.Services.Single(item => item.Id == service.Id).DurationMinutes);
    }

    [Fact]
    public async Task SetupIgnoresArchivedNamesAndOrdersWithoutLocation()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var archived = new OrganizationScenarioCatalogItem
        {
            OrganizationId = organization.Id,
            KindCode = OrganizationScenarioCatalogKindCodes.WorkPost,
            ScenarioCode = "detailing",
            Code = "archived-post",
            Name = "Пост полировки",
            IsActive = false
        };
        var current = new OrganizationScenarioCatalogItem
        {
            OrganizationId = organization.Id,
            KindCode = OrganizationScenarioCatalogKindCodes.WorkPost,
            ScenarioCode = "detailing",
            Code = "current-post",
            Name = "Свободный пост"
        };
        var vehicle = new Vehicle
        {
            Organization = organization,
            User = organization.OwnerUser,
            Name = "Техника без назначенного поста",
            DateTimeCreation = DateTime.UtcNow
        };
        var order = new OpenCRM.Domain.Maintenance
        {
            Vehicle = vehicle,
            User = organization.OwnerUser,
            Date = DateTime.UtcNow,
            DateTimeCreation = DateTime.UtcNow,
            Location = null
        };
        context.OrganizationScenarioCatalogItems.AddRange(archived, current);
        context.Maintenance.Add(order);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICatalogSetupService>().SaveItemsAsync(
            organization.Id, OrganizationScenarioCatalogKindCodes.WorkPost,
            [new CatalogSetupItemDto { Id = current.Id, Name = archived.Name, IsActive = true }]);
        await context.SaveChangesAsync();

        await using var verification = fixture.CreateContext();
        var saved = await verification.OrganizationScenarioCatalogItems.AsNoTracking().SingleAsync(item => item.Id == current.Id);
        Assert.Equal("Пост полировки", saved.Name);
        Assert.True(saved.IsActive);
        Assert.False(await verification.OrganizationScenarioCatalogItems.Where(item => item.Id == archived.Id)
            .Select(item => item.IsActive).SingleAsync());
        Assert.Null(await verification.Maintenance.Where(item => item.Id == order.Id).Select(item => item.Location).SingleAsync());
    }
}
