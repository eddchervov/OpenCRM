using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Modules.Imports.Tests.Integration;

public sealed class ImportMigrationTests(ImportDatabaseFixture fixture) : IClassFixture<ImportDatabaseFixture>
{
    [Fact]
    public async Task SchemaRoundTripRemovesDraftsButPreservesWorkingRecordsAndMatchesSnapshot()
    {
        await using var context = fixture.CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
        var allMigrations = context.Database.GetMigrations().ToArray();
        var draftMigrationIndex = Array.FindIndex(allMigrations, migration => migration.EndsWith("_AddOrganizationImportDrafts"));
        Assert.True(draftMigrationIndex > 0);

        var migrations = new[]
        {
            allMigrations[draftMigrationIndex - 1],
            allMigrations[draftMigrationIndex],
            allMigrations.Single(migration => migration.EndsWith("_AddOrganizationImportBatchStorage")),
            allMigrations.Single(migration => migration.EndsWith("_AddImportProfilesAndSourceIdentity"))
        };

        Assert.EndsWith("_AddOrganizationImportDrafts", migrations[1]);
        Assert.EndsWith("_AddOrganizationImportBatchStorage", migrations[2]);
        Assert.EndsWith("_AddImportProfilesAndSourceIdentity", migrations[3]);
        var user = new ApplicationUser();
        var organization = new Organization { OwnerUser = user, Name = "Откат импорта" };
        var client = new OrganizationClient { Organization = organization, Name = "Сохранённый клиент" };
        var vehicle = new Vehicle { Organization = organization, OrganizationClient = client, User = user, RegistrationPlate = "А001АА", DateTimeCreation = DateTime.UtcNow };
        context.Vehicles.Add(vehicle);
        context.Set<OrganizationImportDraft>().Add(new OrganizationImportDraft
        {
            OrganizationId = organization.Id, UserId = user.Id, FileName = "test.csv", PayloadJson = "{}"
        });
        var batch = new OrganizationImportDraft
        {
            OrganizationId = organization.Id, UserId = user.Id, FileName = "batch.csv", PayloadJson = "{}", StorageVersion = 2
        };
        context.Add(batch);
        context.Add(new OrganizationImportSourceRow { DraftId = batch.Id, TableId = "table1", Number = 2, ValuesJson = "[\"Анна\"]" });
        context.Add(new OrganizationImportRow { DraftId = batch.Id, Id = "row1", Position = 0, Kind = "client", Status = "create", PayloadJson = "{}" });
        context.Add(new OrganizationImportFile { DraftId = batch.Id, Content = [1, 2, 3] });
        context.Add(new OrganizationImportProfile
        {
            OrganizationId = organization.Id, UserId = user.Id, Name = "Профиль", SettingsJson = "{}"
        });
        context.Add(new OrganizationImportIdentity
        {
            OrganizationId = organization.Id, SourceKey = "CRM", Kind = "client", ExternalId = "C1", ClientId = client.Id,
            Fingerprint = new string('A', 64)
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var migrator = context.GetService<IMigrator>();

        try
        {
            await migrator.MigrateAsync(migrations[2]);
            Assert.Contains(migrations[3], await context.Database.GetPendingMigrationsAsync());
            Assert.Equal(2, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM \"OrganizationImportDrafts\"").SingleAsync());
            Assert.True(await context.OrganizationClients.AnyAsync(item => item.Id == client.Id));
            await migrator.MigrateAsync(migrations[1]);
            Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM \"OrganizationImportDrafts\"").SingleAsync());
            await migrator.MigrateAsync(migrations[0]);
            Assert.Contains(migrations[1], await context.Database.GetPendingMigrationsAsync());
            Assert.True(await context.OrganizationClients.AnyAsync(item => item.Id == client.Id));
            Assert.True(await context.Vehicles.AnyAsync(item => item.Id == vehicle.Id && item.OrganizationClientId == client.Id));
        }
        finally
        {
            await migrator.MigrateAsync();
        }

        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Empty(await context.Set<OrganizationImportDraft>().ToListAsync());
    }
}
