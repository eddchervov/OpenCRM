using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Domain;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

public sealed class CatalogUsageMigrationTests
{
    [Fact]
    public async Task MigrationBackfillsEveryHistoricalSourceAndSupportsSchemaRollback()
    {
        var database = new CatalogDatabaseFixture();

        try
        {
            await database.InitializeAsync();
            await using var context = database.CreateContext();
            var organization = await database.CreateOrganizationAsync(context);
            var service = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Service, "Историческая работа");
            var bundledService = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Service, "Работа в наборе");
            var bundle = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.ServiceBundle, "Архивный набор");
            var detailMaterial = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Material, "Материал строки");
            var stockMaterial = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Material, "Материал остатка");
            var movementMaterial = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Material, "Материал движения");
            var post = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.WorkPost, "\tПост истории\r\n");
            var unused = CatalogItem(organization, OrganizationScenarioCatalogKindCodes.Service, "Не использована");
            bundle.IsActive = false;
            bundle.BundleItems.Add(new OrganizationServiceBundleItem
            {
                OrganizationId = organization.Id,
                BundleId = bundle.Id,
                ServiceId = bundledService.Id
            });
            var stock = new OrganizationMaterialStock
            {
                OrganizationId = organization.Id,
                CatalogItemId = stockMaterial.Id,
                Name = stockMaterial.Name,
                Unit = "л"
            };
            context.OrganizationScenarioCatalogItems.AddRange(
                service, bundledService, bundle, detailMaterial, stockMaterial, movementMaterial, post, unused);
            context.OrganizationMaterialStocks.Add(stock);
            context.Maintenance.Add(new OpenCRM.Domain.Maintenance
            {
                User = organization.OwnerUser,
                Vehicle = new Vehicle { User = organization.OwnerUser, Organization = organization },
                Date = DateTime.UtcNow,
                IsDeleted = true,
                Location = "\r ПОСТ ИСТОРИИ \t\n",
                Details =
                [
                    new MaintenanceDetail
                    {
                        Name = service.Name,
                        CatalogItemId = service.Id,
                        MaterialCatalogItemId = detailMaterial.Id
                    }
                ]
            });
            await context.SaveChangesAsync();
            context.OrganizationMaterialMovements.Add(new OrganizationMaterialMovement
            {
                OrganizationId = organization.Id,
                StockId = stock.Id,
                CatalogItemId = movementMaterial.Id,
                Quantity = 1,
                Unit = "л"
            });
            await context.SaveChangesAsync();
            var migrations = context.Database.GetMigrations().ToArray();
            var migrationIndex = Array.FindIndex(migrations, migration =>
                migration.EndsWith("_PersistCatalogFirstUse", StringComparison.Ordinal));

            Assert.True(migrationIndex > 0);
            var previousMigration = migrations[migrationIndex - 1];
            var currentMigration = migrations[migrationIndex];
            var migrator = context.GetService<IMigrator>();

            await migrator.MigrateAsync(previousMigration);
            await migrator.MigrateAsync(currentMigration);
            await migrator.MigrateAsync();

            var flags = await context.OrganizationScenarioCatalogItems.AsNoTracking()
                .Where(item => item.OrganizationId == organization.Id)
                .ToDictionaryAsync(item => item.Id, item => item.HasBeenUsed);
            Assert.True(flags[service.Id]);
            Assert.True(flags[bundledService.Id]);
            Assert.True(flags[detailMaterial.Id]);
            Assert.True(flags[stockMaterial.Id]);
            Assert.True(flags[movementMaterial.Id]);
            Assert.True(flags[post.Id]);
            Assert.False(flags[bundle.Id]);
            Assert.False(flags[unused.Id]);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    private static OrganizationScenarioCatalogItem CatalogItem(Organization organization, string kindCode, string name) => new()
    {
        OrganizationId = organization.Id,
        ScenarioCode = organization.PrimaryScenarioCode,
        KindCode = kindCode,
        Code = "test-" + Guid.NewGuid().ToString("N"),
        Name = name
    };
}
