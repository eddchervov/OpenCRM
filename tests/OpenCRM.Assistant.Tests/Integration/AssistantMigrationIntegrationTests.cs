using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Domain.Assistant;

namespace OpenCRM.Assistant.Tests.Integration;

public sealed class AssistantMigrationIntegrationTests(AssistantIntegrationFixture fixture) : IClassFixture<AssistantIntegrationFixture>
{
    [Fact]
    public async Task AssistantMigration_RollbackAndReapplicationPreserveExistingOrganizationAndAccount()
    {
        const string migrationId = "20261026135550_AddServerAssistant";
        var organizationId = await fixture.CreateOrganizationAsync("migration-roundtrip");
        var userId = await fixture.GetOwnerUserIdAsync(organizationId);
        await using var context = fixture.CreateContext();
        var original = await context.Organizations.AsNoTracking().Where(item => item.Id == organizationId)
            .Select(item => new { item.Id, item.Name, item.OwnerUserId, item.PrimaryScenarioCode }).SingleAsync();
        var migrations = context.Database.GetMigrations().ToArray();
        var migrationIndex = Array.IndexOf(migrations, migrationId);
        Assert.True(migrationIndex > 0);
        var previousMigration = migrations[migrationIndex - 1];
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(previousMigration);

        Assert.DoesNotContain(migrationId, await context.Database.GetAppliedMigrationsAsync());
        Assert.True(await context.Users.AsNoTracking().AnyAsync(item => item.Id == userId));
        Assert.Equal(original, await context.Organizations.AsNoTracking().Where(item => item.Id == organizationId)
            .Select(item => new { item.Id, item.Name, item.OwnerUserId, item.PrimaryScenarioCode }).SingleAsync());

        await migrator.MigrateAsync(migrationId);

        Assert.Contains(migrationId, await context.Database.GetAppliedMigrationsAsync());
        Assert.True(await context.Users.AsNoTracking().AnyAsync(item => item.Id == userId));
        Assert.Equal(original, await context.Organizations.AsNoTracking().Where(item => item.Id == organizationId)
            .Select(item => new { item.Id, item.Name, item.OwnerUserId, item.PrimaryScenarioCode }).SingleAsync());
        Assert.Equal(0, await context.Set<AssistantUsage>().CountAsync());
        Assert.Equal(0, await context.Set<AssistantOrganizationAccess>().CountAsync());
    }
}
