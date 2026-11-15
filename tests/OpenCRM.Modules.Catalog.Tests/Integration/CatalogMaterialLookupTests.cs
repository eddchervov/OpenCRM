using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

[Collection(CatalogDatabaseCollection.Name)]
public sealed class CatalogMaterialLookupTests(CatalogDatabaseFixture fixture)
{
    [Fact]
    public async Task LookupEnforcesOrganizationKindAndActiveStateWithoutTrackingInOneQuery()
    {
        var queryCounter = new CatalogLookupQueryCounter();
        await using var context = fixture.CreateContext(queryCounter);
        var organization = await fixture.CreateOrganizationAsync(context);
        var otherOrganization = await fixture.CreateOrganizationAsync(context);
        var material = CatalogMaterialLookupTests.CreateItem(organization, OrganizationScenarioCatalogKindCodes.Material, "Активный материал", true);
        var archivedMaterial = CatalogMaterialLookupTests.CreateItem(organization, OrganizationScenarioCatalogKindCodes.Material, "Архивный материал", false);
        var service = CatalogMaterialLookupTests.CreateItem(organization, OrganizationScenarioCatalogKindCodes.Service, "Услуга", true);
        var otherMaterial = CatalogMaterialLookupTests.CreateItem(otherOrganization, OrganizationScenarioCatalogKindCodes.Material, "Чужой материал", true);
        context.OrganizationScenarioCatalogItems.AddRange(material, archivedMaterial, service, otherMaterial);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var lookup = scope.ServiceProvider.GetRequiredService<ICatalogMaterialLookup>();
        queryCounter.Reset();

        var activeResult = await lookup.GetMaterialAsync(organization.Id, material.Id, requireActive: true);

        Assert.Equal(new CatalogMaterialLookupDto(material.Id, material.Name), activeResult);
        Assert.Null(await lookup.GetMaterialAsync(organization.Id, archivedMaterial.Id, requireActive: true));
        Assert.Equal(new CatalogMaterialLookupDto(archivedMaterial.Id, archivedMaterial.Name),
            await lookup.GetMaterialAsync(organization.Id, archivedMaterial.Id, requireActive: false));
        Assert.Null(await lookup.GetMaterialAsync(organization.Id, service.Id, requireActive: false));
        Assert.Null(await lookup.GetMaterialAsync(organization.Id, otherMaterial.Id, requireActive: false));
        Assert.Equal(5, queryCounter.ReaderCommandCount);
        Assert.Empty(context.ChangeTracker.Entries<OrganizationScenarioCatalogItem>());
    }

    private static OrganizationScenarioCatalogItem CreateItem(
        Organization organization,
        string kindCode,
        string name,
        bool isActive) => new()
    {
        Organization = organization,
        ScenarioCode = "detailing",
        KindCode = kindCode,
        Code = Guid.NewGuid().ToString("N"),
        Name = name,
        IsActive = isActive
    };

    private sealed class CatalogLookupQueryCounter : DbCommandInterceptor
    {
        internal int ReaderCommandCount { get; private set; }

        internal void Reset() => this.ReaderCommandCount = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            this.ReaderCommandCount++;

            return ValueTask.FromResult(result);
        }
    }
}
