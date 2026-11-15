using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using OpenCRM.Modules.Workspace.Queries;
using Testcontainers.PostgreSql;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class JournalIndexMigrationTests
{
    private const string JournalIndexName = "IX_Maintenance_Journal_EffectiveDate_Order";

    [Fact]
    public async Task ConcurrentJournalIndexSupportsRollbackAndReapplicationWithoutChangingJournalOrBusinessData()
    {
        const string previousMigration = "20261019095140_IndexWorkdayActiveOrders";
        const string migration = "20261019102632_IndexMaintenanceJournalDates";
        await using var postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(postgres.GetConnectionString(), database =>
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name))
            .Options;

        await using var context = new OpenCRMContext(options);
        var migrator = context.GetService<IMigrator>();
        var upScript = migrator.GenerateScript(previousMigration, migration);
        var downScript = migrator.GenerateScript(migration, previousMigration);
        Assert.Contains($"CREATE INDEX CONCURRENTLY \"{JournalIndexMigrationTests.JournalIndexName}\"", upScript);
        Assert.Contains($"DROP INDEX CONCURRENTLY IF EXISTS \"{JournalIndexMigrationTests.JournalIndexName}\"", downScript);
        await migrator.MigrateAsync(previousMigration);

        // Базовая миграция хранит цены в double precision, а текущая предметная модель читает decimal.
        await context.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "MaintenanceDetails" ALTER COLUMN "Price" TYPE numeric(18,2);
            ALTER TABLE "MaintenanceWorkReports" ALTER COLUMN "ApprovedPrice" TYPE numeric(18,2);
            """);

        // Запрос журнала использует nullable-поля идентификации клиента, появившиеся после проверяемой схемы.
        await context.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "OrganizationClients"
                ADD COLUMN "CompanyTypeCode" character varying(16),
                ADD COLUMN "FirstName" character varying(60),
                ADD COLUMN "LastName" character varying(60),
                ADD COLUMN "MiddleName" character varying(60);
            """);

        // Текущий запрос материализует обслуживание с полями приёмки и напоминаний, не участвующими в индексе.
        await context.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Maintenance"
                ADD COLUMN "IntakeDiagramVersion" character varying(64),
                ADD COLUMN "VisitReminderEnabled" boolean NOT NULL DEFAULT true;
            """);
        var user = new ApplicationUser { Email = "journal-migration@example.test" };
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity."AspNetUsers"
                ("Id", "Email", "NormalizedEmail", "DatetimeCreation", "LastDateOfEntry", "CountVisits",
                 "LastLoginDate", "CountLogins", "IsDeleted", "CanUseTestSubscriptions", "EmailConfirmed",
                 "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount",
                 "PersonalDataProcessingConsentAccepted", "AdvertisingConsentAccepted")
            VALUES ({user.Id}, {user.Email}, {user.Email!.ToUpperInvariant()}, now(), now(), 0, now(), 0, false,
                    false, false, false, false, false, 0, false, false)
            """);
        var organization = new Organization
        {
            Name = "Проверка миграции журнала",
            PrimaryScenarioCode = OrganizationTypeCodes.Detailing,
            OwnerUserId = user.Id
        };
        await HistoricalMigrationSeed.InsertOrganizationAsync(context, organization);

        var vehicle = new Vehicle
        {
            UserId = user.Id,
            Organization = organization,
            ManualBrandName = "BMW",
            ManualModelName = "X3",
            RegistrationPlate = "А123АА154"
        };

        var date = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);
        var completed = JournalIndexMigrationTests.Order(
            vehicle, MaintenanceStatus.Completed, date.AddDays(-4), date.AddDays(-3), date.AddHours(23).AddMinutes(59));

        var completedWithoutDate = JournalIndexMigrationTests.Order(
            vehicle, MaintenanceStatus.Completed, date.AddHours(18), date.AddDays(1), null);

        var plannedWithoutDate = JournalIndexMigrationTests.Order(
            vehicle, MaintenanceStatus.Planned, date.AddHours(12), null, null);

        var active = JournalIndexMigrationTests.Order(
            vehicle, MaintenanceStatus.InProgress, date.AddDays(-1), date, null);

        var nextDayCancelled = JournalIndexMigrationTests.Order(
            vehicle, MaintenanceStatus.Cancelled, date.AddDays(-10), date.AddDays(1), null);

        var orders = new[] { completed, completedWithoutDate, plannedWithoutDate, active, nextDayCancelled };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        // Историческая схема ещё не содержит итогов, которые поддерживает текущий SaveChanges.
        foreach (var order in orders)
        {
            await context.Database.ExecuteSqlAsync($"""
                INSERT INTO "Maintenance" ("Id", "Type", "Date", "DateTimeCreation", "IsDeleted", "UserId", "VehicleId",
                    "Status", "PlannedDate", "CompletedDate", "PlannedStartTime", "DiscountAmount")
                VALUES ({order.Id}, 0, {order.Date}, {order.DateTimeCreation}, {order.IsDeleted}, {user.Id}, {vehicle.Id},
                    {(int)order.Status}, {order.PlannedDate}, {order.CompletedDate}, {order.PlannedStartTime}, {order.DiscountAmount});
                INSERT INTO "MaintenanceDetails" ("MaintenanceId", "DetailId", "Position", "Name", "Price", "Quantity")
                VALUES ({order.Id}, {Guid.NewGuid()}, 0, 'Мойка', 150.75, 1);
                """);
        }

        var originalIndexes = await JournalIndexMigrationTests.ReadIndexesAsync(context);
        var originalData = await JournalIndexMigrationTests.ReadBusinessSnapshotAsync(context);
        var originalJournal = await JournalIndexMigrationTests.ReadJournalSnapshotAsync(context, organization.Id);
        var originalUserCount = await context.Users.CountAsync();
        var originalVehicleCount = await context.Vehicles.CountAsync();

        await context.Database.ExecuteSqlRawAsync($"""
            CREATE INDEX "{JournalIndexMigrationTests.JournalIndexName}" ON "Maintenance" ("Id");
            """);

        var migrationTargets = new[] { migration, previousMigration, migration };

        foreach (var target in migrationTargets)
        {
            await migrator.MigrateAsync(target);
            Assert.Null(context.Database.CurrentTransaction);
            var indexes = await JournalIndexMigrationTests.ReadIndexesAsync(context);

            if (target == migration)
            {
                JournalIndexMigrationTests.AssertJournalIndex(indexes);
            }
            else
            {
                Assert.DoesNotContain(indexes, index => index.Name == JournalIndexMigrationTests.JournalIndexName);
            }

            Assert.Equal(
                originalIndexes.Select(index => (index.Name, index.Definition, index.Predicate, index.IsValid, index.IsReady)),
                indexes.Where(index => index.Name != JournalIndexMigrationTests.JournalIndexName)
                    .Select(index => (index.Name, index.Definition, index.Predicate, index.IsValid, index.IsReady)));
            Assert.Equal(originalData, await JournalIndexMigrationTests.ReadBusinessSnapshotAsync(context));
            Assert.Equal(originalJournal, await JournalIndexMigrationTests.ReadJournalSnapshotAsync(context, organization.Id));
            Assert.Equal(originalUserCount, await context.Users.CountAsync());
            Assert.Equal(originalVehicleCount, await context.Vehicles.CountAsync());
            Assert.Equal(target, (await context.Database.GetAppliedMigrationsAsync()).Last());
        }
    }

    private static MaintenanceEntity Order(
        Vehicle vehicle,
        MaintenanceStatus status,
        DateTime date,
        DateTime? plannedDate,
        DateTime? completedDate) => new()
    {
        UserId = vehicle.UserId,
        Vehicle = vehicle,
        Status = status,
        Date = date,
        PlannedDate = plannedDate,
        CompletedDate = completedDate,
        PlannedStartTime = TimeSpan.FromHours(9),
        DateTimeCreation = date,
        DiscountAmount = 1.25m,
        Details = []
    };

    private static async Task<string> ReadJournalSnapshotAsync(OpenCRMContext context, Guid organizationId)
    {
        var service = new OrganizationMaintenanceJournalQueryService(context);
        var date = new DateOnly(2026, 10, 19);
        var firstPage = await service.GetMaintenanceJournalAsync(organizationId, null, date, date, null, 1, 2, false);
        var secondPage = await service.GetMaintenanceJournalAsync(organizationId, null, date, date, null, 2, 2, false);
        var all = await service.GetMaintenanceJournalAsync(organizationId, null, null, null, null, 1, 25, false);

        Assert.Equal(4, firstPage.TotalCount);
        Assert.Equal(4, secondPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.Equal(new[] { MaintenanceStatus.Completed, MaintenanceStatus.Completed }, firstPage.Items.Select(item => item.Status));
        Assert.Equal(new[] { MaintenanceStatus.Planned, MaintenanceStatus.InProgress }, secondPage.Items.Select(item => item.Status));
        Assert.Equal(5, all.TotalCount);
        Assert.Equal(MaintenanceStatus.Cancelled, all.Items[0].Status);

        return JsonSerializer.Serialize(new { firstPage, secondPage, all });
    }

    private static Task<string> ReadBusinessSnapshotAsync(OpenCRMContext context) => context.Database.SqlQuery<string>(
        $"""
        SELECT jsonb_build_object(
            'Maintenance', (SELECT jsonb_agg(to_jsonb(maintenance) ORDER BY maintenance."Id") FROM "Maintenance" AS maintenance),
            'Details', (SELECT jsonb_agg(to_jsonb(detail) ORDER BY detail."MaintenanceId", detail."DetailId")
                FROM "MaintenanceDetails" AS detail))::text AS "Value"
        """).SingleAsync();

    private static Task<IndexMetadata[]> ReadIndexesAsync(OpenCRMContext context) => context.Database.SqlQuery<IndexMetadata>(
        $"""
        SELECT index_relation.relname AS "Name",
            pg_get_indexdef(index_metadata.indexrelid) AS "Definition",
            pg_get_expr(index_metadata.indpred, index_metadata.indrelid) AS "Predicate",
            index_metadata.indoption::smallint[] AS "SortOptions",
            index_metadata.indnkeyatts AS "KeyCount",
            index_metadata.indisvalid AS "IsValid",
            index_metadata.indisready AS "IsReady"
        FROM pg_index AS index_metadata
        JOIN pg_class AS index_relation ON index_relation.oid = index_metadata.indexrelid
        JOIN pg_namespace AS index_namespace ON index_namespace.oid = index_relation.relnamespace
        WHERE index_namespace.nspname = current_schema()
        ORDER BY index_relation.relname
        """).ToArrayAsync();

    private static void AssertJournalIndex(IndexMetadata[] indexes)
    {
        var index = Assert.Single(indexes, item => item.Name == JournalIndexMigrationTests.JournalIndexName);
        var definition = Regex.Replace(index.Definition, @"\s+", " ");
        Assert.True(index.IsValid);
        Assert.True(index.IsReady);
        Assert.Equal(4, index.KeyCount);
        Assert.Equal(4, index.SortOptions.Length);
        const short DescendingNullsFirst = 3;
        Assert.All(index.SortOptions, option => Assert.Equal(DescendingNullsFirst, option));
        Assert.Equal(2, Regex.Matches(definition, "CASE").Count);
        Assert.Equal(2, Regex.Matches(definition, Regex.Escape("WHEN (\"Status\" = 3)")).Count);
        Assert.Contains("THEN COALESCE(\"CompletedDate\", \"Date\")", definition);
        Assert.Contains("ELSE COALESCE(\"PlannedDate\", \"Date\")", definition);
        Assert.Contains("THEN NULL::interval", definition);
        Assert.Contains("ELSE \"PlannedStartTime\"", definition);
        Assert.Contains("\"DateTimeCreation\" DESC, \"Id\" DESC", definition);
        Assert.Contains("INCLUDE (\"VehicleId\", \"Status\", \"CompletedDate\", \"Date\", \"PlannedDate\")", definition);
        Assert.NotNull(index.Predicate);
        Assert.Equal("NOT \"IsDeleted\"", index.Predicate.Trim('(', ')'));
        var datePosition = definition.IndexOf("\"CompletedDate\"", StringComparison.Ordinal);
        var timePosition = definition.IndexOf("\"PlannedStartTime\"", StringComparison.Ordinal);
        var creationPosition = definition.IndexOf("\"DateTimeCreation\"", StringComparison.Ordinal);
        Assert.True(datePosition < timePosition);
        Assert.True(timePosition < creationPosition);
    }

    private sealed class IndexMetadata
    {
        public string Name { get; set; } = string.Empty;

        public string Definition { get; set; } = string.Empty;

        public string? Predicate { get; set; }

        public short[] SortOptions { get; set; } = [];

        public short KeyCount { get; set; }

        public bool IsValid { get; set; }

        public bool IsReady { get; set; }
    }
}
