using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class WorkdayIndexMigrationTests
{
    [Fact]
    public async Task ConcurrentIndexesSupportMigrationRollbackAndReapplicationWithoutChangingBusinessData()
    {
        const string previousMigration = "20261019075758_PersistCatalogFirstUse";
        const string migration = "20261019095140_IndexWorkdayActiveOrders";
        await using var postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(postgres.GetConnectionString(), database =>
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name))
            .Options;

        await using var context = new OpenCRMContext(options);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(previousMigration);
        var user = new ApplicationUser { Email = "workday-migration@example.test" };
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity."AspNetUsers"
                ("Id", "Email", "NormalizedEmail", "DatetimeCreation", "LastDateOfEntry", "CountVisits",
                 "LastLoginDate", "CountLogins", "IsDeleted", "CanUseTestSubscriptions", "EmailConfirmed",
                 "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount",
                 "PersonalDataProcessingConsentAccepted", "AdvertisingConsentAccepted")
            VALUES ({user.Id}, {user.Email}, {user.Email!.ToUpperInvariant()}, now(), now(), 0, now(), 0, false,
                    false, false, false, false, false, 0, false, false)
            """);
        var vehicle = new Vehicle { UserId = user.Id };
        var date = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);

        var orders = new[]
        {
            WorkdayIndexMigrationTests.Order(vehicle, MaintenanceStatus.Planned, date),
            WorkdayIndexMigrationTests.Order(vehicle, MaintenanceStatus.InProgress, date),
            WorkdayIndexMigrationTests.Order(vehicle, MaintenanceStatus.Completed, date, readyAt: date.AddHours(10)),
            WorkdayIndexMigrationTests.Order(vehicle, MaintenanceStatus.Completed, date,
                readyAt: date.AddHours(10), handedOverAt: date.AddHours(11)),
            WorkdayIndexMigrationTests.Order(vehicle, MaintenanceStatus.Planned, date, isDeleted: true)
        };

        await HistoricalMigrationSeed.InsertVehicleAsync(context, vehicle);

        // Исторические заказы создаются до появления поддерживаемых текущим SaveChanges сводок.
        foreach (var order in orders)
        {
            await context.Database.ExecuteSqlAsync($"""
                INSERT INTO "Maintenance" ("Id", "Type", "Date", "DateTimeCreation", "IsDeleted", "UserId", "VehicleId",
                    "Status", "PlannedDate", "PlannedStartTime", "ReadyForHandoverAt", "HandedOverAt")
                VALUES ({order.Id}, 0, {order.Date}, {order.DateTimeCreation}, {order.IsDeleted}, {user.Id}, {vehicle.Id},
                    {(int)order.Status}, {order.PlannedDate}, {order.PlannedStartTime}, {order.ReadyForHandoverAt}, {order.HandedOverAt});
                """);
        }

        var originalIndexes = await WorkdayIndexMigrationTests.ReadIndexesAsync(context);
        var originalData = await WorkdayIndexMigrationTests.ReadMaintenanceSnapshotAsync(context);
        var originalUserCount = await context.Users.CountAsync();
        var originalVehicleCount = await context.Vehicles.CountAsync();

        await context.Database.ExecuteSqlRawAsync("""
            CREATE INDEX "IX_Maintenance_Workday_Planned" ON "Maintenance" ("Id");
            """);

        await migrator.MigrateAsync(migration);
        Assert.Null(context.Database.CurrentTransaction);
        WorkdayIndexMigrationTests.AssertWorkdayIndexes(await WorkdayIndexMigrationTests.ReadIndexesAsync(context));
        Assert.Equal(originalData, await WorkdayIndexMigrationTests.ReadMaintenanceSnapshotAsync(context));

        await migrator.MigrateAsync(previousMigration);
        var rolledBackIndexes = await WorkdayIndexMigrationTests.ReadIndexesAsync(context);

        Assert.DoesNotContain(rolledBackIndexes, index => index.Name.StartsWith("IX_Maintenance_Workday_", StringComparison.Ordinal));
        Assert.Equal(
            originalIndexes.Select(index => (index.Name, index.Definition, index.Predicate, index.IsValid, index.IsReady)),
            rolledBackIndexes.Select(index => (index.Name, index.Definition, index.Predicate, index.IsValid, index.IsReady)));
        Assert.Equal(originalData, await WorkdayIndexMigrationTests.ReadMaintenanceSnapshotAsync(context));

        await migrator.MigrateAsync(migration);
        WorkdayIndexMigrationTests.AssertWorkdayIndexes(await WorkdayIndexMigrationTests.ReadIndexesAsync(context));
        Assert.Equal(originalData, await WorkdayIndexMigrationTests.ReadMaintenanceSnapshotAsync(context));
        Assert.Equal(5, await context.Maintenance.CountAsync());
        Assert.Equal(originalUserCount, await context.Users.CountAsync());
        Assert.Equal(originalVehicleCount, await context.Vehicles.CountAsync());
        Assert.Equal(migration, (await context.Database.GetAppliedMigrationsAsync()).Last());
    }

    private static OpenCRM.Domain.Maintenance Order(
        Vehicle vehicle,
        MaintenanceStatus status,
        DateTime date,
        DateTime? readyAt = null,
        DateTime? handedOverAt = null,
        bool isDeleted = false) => new()
    {
        UserId = vehicle.UserId,
        Vehicle = vehicle,
        Status = status,
        Date = date,
        PlannedDate = date.AddDays(1),
        PlannedStartTime = TimeSpan.FromHours(9),
        ReadyForHandoverAt = readyAt,
        HandedOverAt = handedOverAt,
        IsDeleted = isDeleted,
        Details = []
    };

    private static Task<string> ReadMaintenanceSnapshotAsync(OpenCRMContext context) => context.Database.SqlQuery<string>(
        $"""
        SELECT COALESCE(jsonb_agg(to_jsonb(maintenance) ORDER BY maintenance."Id"), '[]'::jsonb)::text AS "Value"
        FROM "Maintenance" AS maintenance
        """).SingleAsync();

    private static Task<IndexMetadata[]> ReadIndexesAsync(OpenCRMContext context) => context.Database.SqlQuery<IndexMetadata>(
        $"""
        SELECT index_relation.relname AS "Name",
            pg_get_indexdef(index_metadata.indexrelid) AS "Definition",
            pg_get_expr(index_metadata.indpred, index_metadata.indrelid) AS "Predicate",
            index_metadata.indisvalid AS "IsValid",
            index_metadata.indisready AS "IsReady"
        FROM pg_index AS index_metadata
        JOIN pg_class AS index_relation ON index_relation.oid = index_metadata.indexrelid
        JOIN pg_namespace AS index_namespace ON index_namespace.oid = index_relation.relnamespace
        WHERE index_namespace.nspname = current_schema()
        ORDER BY index_relation.relname
        """).ToArrayAsync();

    private static void AssertWorkdayIndexes(IndexMetadata[] indexes)
    {
        Assert.Equal(4, indexes.Count(index => index.Name.StartsWith("IX_Maintenance_Workday_", StringComparison.Ordinal)));

        var expected = new[]
        {
            (Name: "Planned", Status: 1, Column: "PlannedDate"),
            (Name: "InProgress", Status: 2, Column: "PlannedDate"),
            (Name: "Ready", Status: 3, Column: "ReadyForHandoverAt"),
            (Name: "HandedOver", Status: 3, Column: "HandedOverAt")
        };

        foreach (var value in expected)
        {
            var index = Assert.Single(indexes, item => item.Name == "IX_Maintenance_Workday_" + value.Name);
            Assert.True(index.IsValid);
            Assert.True(index.IsReady);
            Assert.Contains("\"Maintenance\"", index.Definition);
            Assert.Contains("\"" + value.Column + "\"", index.Definition);
            Assert.Contains("DESC", index.Definition);
            Assert.Contains("INCLUDE (\"VehicleId\")", index.Definition);
            Assert.NotNull(index.Predicate);
            Assert.Contains("\"Status\" = " + value.Status, index.Predicate);
            Assert.Contains("NOT \"IsDeleted\"", index.Predicate);
        }

        var planned = indexes.Single(index => index.Name == "IX_Maintenance_Workday_Planned");
        var inProgress = indexes.Single(index => index.Name == "IX_Maintenance_Workday_InProgress");
        var ready = indexes.Single(index => index.Name == "IX_Maintenance_Workday_Ready");
        var handedOver = indexes.Single(index => index.Name == "IX_Maintenance_Workday_HandedOver");
        Assert.Contains("COALESCE", planned.Definition);
        Assert.Contains("\"Date\"", planned.Definition);
        Assert.Contains("COALESCE", inProgress.Definition);
        Assert.Contains("\"Date\"", inProgress.Definition);
        Assert.Contains("\"ReadyForHandoverAt\" IS NOT NULL", ready.Predicate);
        Assert.Contains("\"HandedOverAt\" IS NULL", ready.Predicate);
        Assert.Contains("\"HandedOverAt\" IS NOT NULL", handedOver.Predicate);
    }

    private sealed class IndexMetadata
    {
        public string Name { get; set; } = string.Empty;

        public string Definition { get; set; } = string.Empty;

        public string? Predicate { get; set; }

        public bool IsValid { get; set; }

        public bool IsReady { get; set; }
    }
}
