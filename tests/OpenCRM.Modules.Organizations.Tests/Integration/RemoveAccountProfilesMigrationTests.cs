using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

public sealed class RemoveAccountProfilesMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261014114538_SeparateAppliedDiscountValidity";
    private const string TargetMigration = "20261015071909_RemoveAccountProfiles";
    private static readonly string[] s_removedTables =
    [
        "AccountScenarioFeatures",
        "UserAccountFeatures",
        "AccountFeatureDefinitions",
        "AccountScenarioDefinitions",
        "UserAccountProfiles"
    ];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        await using var context = this.CreateContext();
        await RemoveAccountProfilesMigrationTests.GetMigrator(context).MigrateAsync(PreviousMigration);
    }

    public Task DisposeAsync() => this._postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task MigrationPreservesOrganizationTypesAndRestoresLegacyDataFromBackupAfterRollback()
    {
        var userId = await this.SeedAsync(includeDetailing: true);
        await using var context = this.CreateContext();
        var migrator = RemoveAccountProfilesMigrationTests.GetMigrator(context);
        await this.CreateBackupsAsync(context);

        await migrator.MigrateAsync(TargetMigration);

        Assert.True(await this.OrganizationTypeMatchesAsync(context, "detailing", "Студия", false, 31, true));
        Assert.True(await this.OrganizationTypeMatchesAsync(context, "service-station", "Мастерская", true, 10, true));
        Assert.True(await this.OrganizationTypeMatchesAsync(context, "fleet", "Автопарк клиента", false, 75, true));
        Assert.True(await this.OrganizationTypeMatchesAsync(context, "wrapping", "Оклейка", true, 88, false));
        Assert.Equal(3, await this.SupportedTypeCountAsync(context));

        var organizationCodes = await context.Database.SqlQuery<string>($"""
            SELECT "PrimaryScenarioCode" AS "Value"
            FROM "Organizations"
            WHERE "OwnerUserId" = {userId}
            ORDER BY "Name"
            """).ToArrayAsync();
        Assert.Equal(new[] { "service-station", "wrapping" }, organizationCodes.OrderBy(code => code, StringComparer.Ordinal));

        foreach (var table in RemoveAccountProfilesMigrationTests.s_removedTables)
        {
            Assert.False(await this.TableExistsAsync(context, table));
        }

        await migrator.MigrateAsync(PreviousMigration);

        foreach (var table in RemoveAccountProfilesMigrationTests.s_removedTables)
        {
            Assert.True(await this.TableExistsAsync(context, table));
        }

        Assert.False(await this.TableExistsAsync(context, "OrganizationTypeDefinitions"));
        Assert.Equal(4, await context.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM \"AccountScenarioDefinitions\"").SingleAsync());
        Assert.True(await this.LegacyTypeMatchesAsync(context, "detailing", "Студия", false, 31));
        Assert.True(await this.LegacyTypeMatchesAsync(context, "service-station", "Мастерская", true, 10));
        Assert.True(await this.LegacyTypeMatchesAsync(context, "fleet", "Автопарк клиента", false, 75));
        Assert.True(await this.LegacyTypeMatchesAsync(context, "wrapping", "Оклейка", true, 88));

        await this.RestoreBackupsAsync(context);
        Assert.Equal(1, await context.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM \"UserAccountProfiles\"").SingleAsync());
        Assert.Equal(1, await context.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM \"UserAccountFeatures\"").SingleAsync());
        Assert.Equal(1, await context.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM \"AccountScenarioFeatures\"").SingleAsync());
        Assert.True(await context.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1 FROM "UserAccountProfiles"
                WHERE "UserId" = {userId} AND "PrimaryScenarioCode" = 'service-station')
                AND EXISTS (
                    SELECT 1 FROM "UserAccountFeatures"
                    WHERE "UserAccountProfileId" = 1 AND "FeatureCode" = 'clients')
                AND EXISTS (
                    SELECT 1 FROM "AccountScenarioFeatures"
                    WHERE "AccountScenarioDefinitionId" = 2 AND "AccountFeatureDefinitionId" = 1
                        AND "SortOrder" = 1) AS "Value"
            """).SingleAsync());

        await migrator.MigrateAsync(TargetMigration);

        Assert.True(await this.OrganizationTypeMatchesAsync(context, "wrapping", "Оклейка", true, 88, false));
        foreach (var table in RemoveAccountProfilesMigrationTests.s_removedTables)
        {
            Assert.False(await this.TableExistsAsync(context, table));
        }
    }

    [Fact]
    public async Task MigrationCreatesMissingSupportedTypeAndRejectsUnsupportedSupportedCode()
    {
        await this.SeedAsync(includeDetailing: false);
        await using var context = this.CreateContext();
        var migrator = RemoveAccountProfilesMigrationTests.GetMigrator(context);

        await migrator.MigrateAsync(TargetMigration);

        Assert.True(await this.OrganizationTypeMatchesAsync(context, "detailing", "Детейлинг", true, 30, true));
        Assert.Equal(3, await this.SupportedTypeCountAsync(context));
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "OrganizationTypeDefinitions"
                ("Code", "Name", "Description", "IsActive", "IsSupported", "SortOrder", "CreatedAt", "UpdatedAt")
            VALUES ('unsupported', 'Несуществующий тип', '', TRUE, TRUE, 99, NOW(), NOW())
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("CK_OrganizationTypeDefinitions_SupportedCode", error.ConstraintName);

        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "OrganizationTypeDefinitions"
                ("Code", "Name", "Description", "IsActive", "IsSupported", "SortOrder", "CreatedAt", "UpdatedAt")
            VALUES ('unsupported', 'Исторический тип', '', TRUE, FALSE, 99, NOW(), NOW())
            """);
        Assert.True(await this.OrganizationTypeMatchesAsync(context, "unsupported", "Исторический тип", true, 99, false));
    }

    private OpenCRMContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(this._postgres.GetConnectionString(), db =>
                db.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name))
            .Options;

        return new OpenCRMContext(options);
    }

    private async Task<string> SeedAsync(bool includeDetailing)
    {
        var userId = Guid.NewGuid().ToString();
        await using var context = this.CreateContext();
        var userName = $"migration-{userId}@example.test";
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity."AspNetUsers"
                ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "DatetimeCreation",
                 "LastDateOfEntry", "CountVisits", "LastLoginDate", "CountLogins", "IsDeleted",
                 "CanUseTestSubscriptions", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled",
                 "LockoutEnabled", "AccessFailedCount", "PersonalDataProcessingConsentAccepted",
                 "AdvertisingConsentAccepted", "SecurityStamp")
            VALUES ({userId}, {userName}, {userName.ToUpperInvariant()}, {userName}, {userName.ToUpperInvariant()},
                    now(), now(), 0, now(), 0, false, false, true, false, false, false, 0, false, false,
                    {Guid.NewGuid().ToString()})
            """);

        // Старая схема не содержит полей, добавленных последующими миграциями текущей модели.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Organizations"
                ("Name", "PrimaryScenarioCode", "OwnerUserId", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES ('СТО', 'service-station', {userId}, TRUE, NOW(), NOW()),
                   ('Оклейка', 'wrapping', {userId}, TRUE, NOW(), NOW());
            """);

        await context.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE "AccountScenarioFeatures", "UserAccountFeatures", "AccountFeatureDefinitions",
                "AccountScenarioDefinitions", "UserAccountProfiles" RESTART IDENTITY CASCADE
            """);

        if (includeDetailing)
        {
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO "AccountScenarioDefinitions"
                    ("Id", "Code", "Name", "Description", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt")
                VALUES (1, 'detailing', 'Студия', 'Описание detailing', FALSE, 31, NOW(), NOW()),
                       (2, 'service-station', 'Мастерская', 'Описание СТО', TRUE, 10, NOW(), NOW()),
                       (3, 'fleet', 'Автопарк клиента', 'Описание fleet', FALSE, 75, NOW(), NOW()),
                       (4, 'wrapping', 'Оклейка', 'Исторический тип', TRUE, 88, NOW(), NOW());
                """);
        }
        else
        {
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO "AccountScenarioDefinitions"
                    ("Id", "Code", "Name", "Description", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt")
                VALUES (2, 'service-station', 'Мастерская', 'Описание СТО', TRUE, 10, NOW(), NOW()),
                       (3, 'fleet', 'Автопарк клиента', 'Описание fleet', FALSE, 75, NOW(), NOW());
                """);
        }

        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AccountFeatureDefinitions"
                ("Id", "AudienceCode", "Code", "Name", "Description", "FeatureTypeCode",
                    "IsAvailableNow", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt")
            VALUES (1, 'organization', 'clients', 'Клиенты', '', 'boolean', TRUE, TRUE, 1, NOW(), NOW());
            """);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "UserAccountProfiles"
                ("Id", "UserId", "AccountMode", "PrimaryScenarioCode", "CreatedAt", "UpdatedAt")
            VALUES (1, {userId}, 'organization', 'service-station', NOW(), NOW());
            """);

        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "UserAccountFeatures" ("Id", "UserAccountProfileId", "FeatureCode", "EnabledAt")
            VALUES (1, 1, 'clients', NOW())
            """);

        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AccountScenarioFeatures" ("Id", "AccountScenarioDefinitionId", "AccountFeatureDefinitionId", "SortOrder")
            VALUES (1, 2, 1, 1);
            """);

        return userId;
    }

    private async Task CreateBackupsAsync(OpenCRMContext context)
    {
        foreach (var table in RemoveAccountProfilesMigrationTests.s_removedTables)
        {
            var backupTableSql = $"CREATE TABLE \"Backup_{table}\" AS TABLE \"{table}\"";
            await context.Database.ExecuteSqlRawAsync(backupTableSql);
        }
    }

    private async Task RestoreBackupsAsync(OpenCRMContext context)
    {
        await context.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE "AccountScenarioFeatures", "UserAccountFeatures", "AccountFeatureDefinitions",
                "AccountScenarioDefinitions", "UserAccountProfiles" RESTART IDENTITY CASCADE
            """);

        var restoreOrder = new[]
        {
            "AccountFeatureDefinitions",
            "AccountScenarioDefinitions",
            "UserAccountProfiles",
            "AccountScenarioFeatures",
            "UserAccountFeatures"
        };

        foreach (var table in restoreOrder)
        {
            var backupTableName = $"Backup_{table}";
            var backupColumns = await context.Database.SqlQuery<string>($"""
                SELECT string_agg(format('%I', column_name), ',') AS "Value"
                FROM information_schema.columns
                WHERE table_schema = current_schema() AND table_name = {backupTableName}
                """).SingleAsync();
            var restoreTableSql = $"INSERT INTO \"{table}\" ({backupColumns}) SELECT {backupColumns} FROM \"{backupTableName}\"";
            await context.Database.ExecuteSqlRawAsync(restoreTableSql);

            var resetIdentitySql = $"""
                SELECT setval(pg_get_serial_sequence('"{table}"', 'Id'),
                    COALESCE((SELECT MAX("Id") FROM "{table}"), 0) + 1, FALSE)
                """;
            await context.Database.ExecuteSqlRawAsync(resetIdentitySql);
        }
    }

    private async Task<bool> OrganizationTypeMatchesAsync(
        OpenCRMContext context,
        string code,
        string name,
        bool isActive,
        int sortOrder,
        bool isSupported)
    {
        return await context.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1
                FROM "OrganizationTypeDefinitions"
                WHERE "Code" = {code}
                    AND "Name" = {name}
                    AND "IsActive" = {isActive}
                    AND "SortOrder" = {sortOrder}
                    AND "IsSupported" = {isSupported}) AS "Value"
            """).SingleAsync();
    }

    private async Task<int> SupportedTypeCountAsync(OpenCRMContext context)
    {
        return await context.Database.SqlQuery<int>($"""
            SELECT COUNT(*)::int AS "Value"
            FROM "OrganizationTypeDefinitions"
            WHERE "IsSupported"
            """).SingleAsync();
    }

    private async Task<bool> LegacyTypeMatchesAsync(
        OpenCRMContext context,
        string code,
        string name,
        bool isActive,
        int sortOrder)
    {
        return await context.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1
                FROM "AccountScenarioDefinitions"
                WHERE "Code" = {code}
                    AND "Name" = {name}
                    AND "IsActive" = {isActive}
                    AND "SortOrder" = {sortOrder}) AS "Value"
            """).SingleAsync();
    }

    private async Task<bool> TableExistsAsync(OpenCRMContext context, string table)
    {
        var qualifiedTableName = $"\"{table}\"";
        return await context.Database.SqlQuery<bool>($"SELECT to_regclass({qualifiedTableName}) IS NOT NULL AS \"Value\"").SingleAsync();
    }

    private static Microsoft.EntityFrameworkCore.Migrations.IMigrator GetMigrator(OpenCRMContext context)
    {
        return context.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
    }
}
