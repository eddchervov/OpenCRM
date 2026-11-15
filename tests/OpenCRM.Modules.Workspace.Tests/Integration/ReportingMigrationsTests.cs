using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class ReportingMigrationsTests
{
    private const string PreviousMigration = "20261019102632_IndexMaintenanceJournalDates";
    private const string MoneyMigration = "20261019110052_UseDecimalMaintenancePrices";

    [Fact]
    public async Task MoneyAndReportingMigrationsPreserveAmountsAcrossRollbackAndReapplication()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await postgres.StartAsync();
        await using var context = ReportingMigrationsTests.CreateContext(postgres.GetConnectionString());
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(ReportingMigrationsTests.PreviousMigration);
        await ReportingMigrationsTests.SeedLegacyDataAsync(context);
        var original = await ReportingMigrationsTests.ReadBusinessSnapshotAsync(context);
        var reportingMigration = context.Database.GetMigrations().Single(name => name.EndsWith("_IndexWorkspaceReportingDates"));
        var targets = new[] { ReportingMigrationsTests.MoneyMigration, reportingMigration,
            ReportingMigrationsTests.PreviousMigration, ReportingMigrationsTests.MoneyMigration, reportingMigration };

        foreach (var target in targets)
        {
            await migrator.MigrateAsync(target);
            Assert.Null(context.Database.CurrentTransaction);
            Assert.Equal(original, await ReportingMigrationsTests.ReadBusinessSnapshotAsync(context));
            Assert.Equal(target, (await context.Database.GetAppliedMigrationsAsync()).Last());
            var columns = await ReportingMigrationsTests.ReadMoneyColumnsAsync(context);

            if (target == ReportingMigrationsTests.PreviousMigration)
            {
                Assert.All(columns, column => Assert.Equal("double precision", column.DataType));
            }
            else
            {
                Assert.All(columns, column =>
                {
                    Assert.Equal("numeric", column.DataType);
                    Assert.Equal(18, column.Precision);
                    Assert.Equal(2, column.Scale);
                });

                var prices = await context.Database.SqlQuery<decimal?>(
                    $"""
                    SELECT "Price" AS "Value" FROM "MaintenanceDetails" ORDER BY "Position"
                    """).ToArrayAsync();

                Assert.Equal(new decimal?[] { 0.1m, 123456.78m, null }, prices);
                Assert.Equal(123456.78m, await context.MaintenanceWorkReports.Select(report => report.ApprovedPrice).SingleAsync());
            }

            await ReportingMigrationsTests.AssertReportingIndexesAsync(context, target == reportingMigration);
            await ReportingMigrationsTests.AssertReportingStatisticsAsync(context, target == reportingMigration);
        }
    }

    [Fact]
    public async Task MoneyMigrationRejectsInvalidLegacyValuesAtomicallyAndAllowsRetry()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await postgres.StartAsync();
        await using var context = ReportingMigrationsTests.CreateContext(postgres.GetConnectionString());
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(ReportingMigrationsTests.PreviousMigration);
        await ReportingMigrationsTests.SeedLegacyDataAsync(context);
        var invalidValues = new[] { "NaN", "Infinity", "-Infinity", "1.001", "10000000000000000" };
        var columns = new[] { (Table: "MaintenanceDetails", Column: "Price"),
            (Table: "MaintenanceWorkReports", Column: "ApprovedPrice") };

        foreach (var column in columns)
        {
            foreach (var invalid in invalidValues)
            {
                if (column.Table == "MaintenanceDetails")
                {
                    await context.Database.ExecuteSqlAsync($"""UPDATE "MaintenanceDetails" SET "Price" = {invalid}::double precision""");
                }
                else
                {
                    await context.Database.ExecuteSqlAsync($"""UPDATE "MaintenanceWorkReports" SET "ApprovedPrice" = {invalid}::double precision""");
                }
                var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                    migrator.MigrateAsync(ReportingMigrationsTests.MoneyMigration));

                Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
                Assert.Equal(ReportingMigrationsTests.PreviousMigration,
                    (await context.Database.GetAppliedMigrationsAsync()).Last());
                Assert.All(await ReportingMigrationsTests.ReadMoneyColumnsAsync(context),
                    item => Assert.Equal("double precision", item.DataType));
                await context.Database.ExecuteSqlRawAsync("""
                    UPDATE "MaintenanceDetails" SET "Price" = 0.1::double precision;
                    UPDATE "MaintenanceWorkReports" SET "ApprovedPrice" = 0.1::double precision;
                    """);
            }
        }

        await migrator.MigrateAsync(ReportingMigrationsTests.MoneyMigration);
        Assert.All(await ReportingMigrationsTests.ReadMoneyColumnsAsync(context),
            column => Assert.Equal("numeric", column.DataType));
    }

    [Fact]
    public async Task MoneyRollbackRejectsLossOfNewDecimalPrecisionAndAllowsRetryAfterCorrection()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await postgres.StartAsync();
        await using var context = ReportingMigrationsTests.CreateContext(postgres.GetConnectionString());
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(ReportingMigrationsTests.PreviousMigration);
        await ReportingMigrationsTests.SeedLegacyDataAsync(context);
        await migrator.MigrateAsync(ReportingMigrationsTests.MoneyMigration);
        var updates = new[]
        {
            """UPDATE "MaintenanceDetails" SET "Price" = 99999999999999.99""",
            """UPDATE "MaintenanceWorkReports" SET "ApprovedPrice" = 99999999999999.99"""
        };

        foreach (var update in updates)
        {
            await context.Database.ExecuteSqlRawAsync(update);
            var original = await ReportingMigrationsTests.ReadBusinessSnapshotAsync(context);
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                migrator.MigrateAsync(ReportingMigrationsTests.PreviousMigration));

            Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
            Assert.Equal(original, await ReportingMigrationsTests.ReadBusinessSnapshotAsync(context));
            Assert.Equal(ReportingMigrationsTests.MoneyMigration,
                (await context.Database.GetAppliedMigrationsAsync()).Last());
            Assert.All(await ReportingMigrationsTests.ReadMoneyColumnsAsync(context),
                column => Assert.Equal("numeric", column.DataType));
            await context.Database.ExecuteSqlRawAsync("""
                UPDATE "MaintenanceDetails" SET "Price" = 0.1;
                UPDATE "MaintenanceWorkReports" SET "ApprovedPrice" = 0.1;
                """);
        }

        await migrator.MigrateAsync(ReportingMigrationsTests.PreviousMigration);
        Assert.All(await ReportingMigrationsTests.ReadMoneyColumnsAsync(context),
            column => Assert.Equal("double precision", column.DataType));
    }

    private static OpenCRMContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(connectionString, database =>
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name))
            .Options;

        return new OpenCRMContext(options);
    }

    private static async Task SeedLegacyDataAsync(OpenCRMContext context)
    {
        var user = new ApplicationUser { Email = "reporting-migration@example.test" };
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity."AspNetUsers"
                ("Id", "Email", "NormalizedEmail", "DatetimeCreation", "LastDateOfEntry", "CountVisits",
                 "LastLoginDate", "CountLogins", "IsDeleted", "CanUseTestSubscriptions", "EmailConfirmed",
                 "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount",
                 "PersonalDataProcessingConsentAccepted", "AdvertisingConsentAccepted")
            VALUES ({user.Id}, {user.Email}, {user.Email!.ToUpperInvariant()}, now(), now(), 0, now(), 0, false,
                    false, false, false, false, false, 0, false, false)
            """);
        var vehicle = new Vehicle { UserId = user.Id, ManualBrandName = "BMW", ManualModelName = "X3" };
        var date = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);
        var order = new OpenCRM.Domain.Maintenance
        {
            UserId = user.Id,
            Vehicle = vehicle,
            Date = date,
            DateTimeCreation = date,
            Details = []
        };

        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "Maintenance" ("Id", "Type", "Date", "DateTimeCreation", "IsDeleted", "UserId", "VehicleId", "Status")
            VALUES ({order.Id}, 0, {order.Date}, {order.DateTimeCreation}, {order.IsDeleted}, {user.Id}, {vehicle.Id}, {(int)order.Status});
            """);

        context.MaintenanceWorkReports.Add(new MaintenanceWorkReport
        {
            MaintenanceId = order.Id,
            CommandId = Guid.NewGuid(),
            Kind = "work",
            CreatedAt = date
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        // Исторические строки создаются напрямую до появления таблицы производных итогов.
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "MaintenanceDetails" ("MaintenanceId", "DetailId", "Position", "Name", "Price", "Quantity") VALUES
                ({order.Id}, {Guid.NewGuid()}, 0, 'Первая', 0.1::double precision, 1),
                ({order.Id}, {Guid.NewGuid()}, 1, 'Вторая', 123456.78::double precision, 1),
                ({order.Id}, {Guid.NewGuid()}, 2, 'Без цены', NULL, 1);
            UPDATE "MaintenanceWorkReports" SET "ApprovedPrice" = 123456.78::double precision;
            """);
    }

    private static Task<string> ReadBusinessSnapshotAsync(OpenCRMContext context) => context.Database.SqlQuery<string>(
        $"""
        SELECT jsonb_build_object(
            'users', (SELECT count(*) FROM identity."AspNetUsers"),
            'vehicles', (SELECT count(*) FROM "Vehicles"),
            'orders', (SELECT count(*) FROM "Maintenance"),
            'details', (SELECT count(*) FROM "MaintenanceDetails"),
            'reports', (SELECT count(*) FROM "MaintenanceWorkReports"),
            'revenueCents', (SELECT sum(round("Price"::numeric * 100))::bigint FROM "MaintenanceDetails"),
            'approvedCents', (SELECT sum(round("ApprovedPrice"::numeric * 100))::bigint FROM "MaintenanceWorkReports")
        )::text AS "Value"
        """).SingleAsync();

    private static Task<MoneyColumn[]> ReadMoneyColumnsAsync(OpenCRMContext context) => context.Database.SqlQuery<MoneyColumn>(
        $"""
        SELECT data_type AS "DataType", numeric_precision::int AS "Precision", numeric_scale::int AS "Scale"
        FROM information_schema.columns
        WHERE table_schema = current_schema() AND
            ((table_name = 'MaintenanceDetails' AND column_name = 'Price') OR
             (table_name = 'MaintenanceWorkReports' AND column_name = 'ApprovedPrice'))
        """).ToArrayAsync();

    private static async Task AssertReportingIndexesAsync(OpenCRMContext context, bool expected)
    {
        var indexes = await context.Database.SqlQuery<ReportingIndex>(
            $"""
            SELECT relation.relname AS "Name", pg_get_indexdef(metadata.indexrelid) AS "Definition",
                pg_get_expr(metadata.indpred, metadata.indrelid) AS "Predicate",
                metadata.indisvalid AS "IsValid", metadata.indisready AS "IsReady",
                metadata.indnkeyatts AS "KeyCount"
            FROM pg_index metadata JOIN pg_class relation ON relation.oid = metadata.indexrelid
            WHERE relation.relname IN ('IX_Maintenance_Finance_Date',
                'IX_Maintenance_Analytics_CompletedDate', 'IX_Maintenance_Analytics_CreatedDate')
            ORDER BY relation.relname
            """).ToArrayAsync();

        if (!expected)
        {
            Assert.Empty(indexes);

            return;
        }

        Assert.Equal(3, indexes.Length);
        Assert.All(indexes, index =>
        {
            Assert.True(index.IsValid);
            Assert.True(index.IsReady);
            Assert.Contains("INCLUDE", index.Definition);
            Assert.Contains("IsDeleted", index.Predicate);
        });
        var finance = Assert.Single(indexes, index => index.Name == "IX_Maintenance_Finance_Date");
        Assert.Equal(2, finance.KeyCount);
        Assert.Contains("COALESCE(\"CompletedDate\", \"PlannedDate\", \"Date\")", finance.Definition);
        Assert.Contains("DESC, \"Id\") INCLUDE (\"VehicleId\", \"CompletedDate\", \"PlannedDate\", \"Date\", " +
            "\"BuyerClientId\", \"BuyerName\", \"DiscountAmount\", \"PaidAmount\", \"MaterialCostAmount\", \"LaborCostAmount\")",
            finance.Definition);
        Assert.Equal("NOT \"IsDeleted\"", finance.Predicate!.Trim('(', ')'));
        var completed = Assert.Single(indexes, index => index.Name == "IX_Maintenance_Analytics_CompletedDate");
        Assert.Equal(1, completed.KeyCount);
        Assert.Contains("COALESCE(\"CompletedDate\", \"Date\")", completed.Definition);
        Assert.Contains("INCLUDE (\"Id\", \"VehicleId\", \"CompletedDate\", \"Date\", \"PaidAmount\", \"DiscountAmount\")",
            completed.Definition);
        Assert.Contains("\"Status\" = 3", completed.Predicate);
        var created = Assert.Single(indexes, index => index.Name == "IX_Maintenance_Analytics_CreatedDate");
        Assert.Equal(1, created.KeyCount);
        Assert.Contains("(\"DateTimeCreation\") INCLUDE (\"VehicleId\")", created.Definition);
        Assert.Equal("NOT \"IsDeleted\"", created.Predicate!.Trim('(', ')'));
    }

    private static async Task AssertReportingStatisticsAsync(OpenCRMContext context, bool expected)
    {
        var statistics = await context.Database.SqlQuery<ReportingStatistics>(
            $"""
            SELECT statistics.stxname AS "Name", pg_get_statisticsobjdef(statistics.oid) AS "Definition",
                pg_get_expr(statistics.stxexprs, statistics.stxrelid) AS "Expression",
                data.stxdexpr IS NOT NULL AS "IsAnalyzed"
            FROM pg_statistic_ext statistics
            LEFT JOIN pg_statistic_ext_data data ON data.stxoid = statistics.oid
            WHERE statistics.stxname IN ('ST_Maintenance_Finance_Date', 'ST_Maintenance_Analytics_CompletedDate')
            ORDER BY statistics.stxname
            """).ToArrayAsync();

        if (!expected)
        {
            Assert.Empty(statistics);

            return;
        }

        Assert.Equal(2, statistics.Length);
        Assert.All(statistics, item =>
        {
            Assert.True(item.IsAnalyzed);
            Assert.Contains($"CREATE STATISTICS public.\"{item.Name}\" ON", item.Definition);
            Assert.Contains("FROM \"Maintenance\"", item.Definition);
        });
        var finance = Assert.Single(statistics, item => item.Name == "ST_Maintenance_Finance_Date");
        Assert.Equal("COALESCE(\"CompletedDate\", \"PlannedDate\", \"Date\")", finance.Expression);
        var completed = Assert.Single(statistics, item => item.Name == "ST_Maintenance_Analytics_CompletedDate");
        Assert.Equal("COALESCE(\"CompletedDate\", \"Date\")", completed.Expression);
    }

    private sealed class ReportingStatistics
    {
        public string Name { get; set; } = string.Empty;

        public string Definition { get; set; } = string.Empty;

        public string Expression { get; set; } = string.Empty;

        public bool IsAnalyzed { get; set; }
    }

    private sealed class MoneyColumn
    {
        public string DataType { get; set; } = string.Empty;

        public int? Precision { get; set; }

        public int? Scale { get; set; }
    }

    private sealed class ReportingIndex
    {
        public string Name { get; set; } = string.Empty;

        public string Definition { get; set; } = string.Empty;

        public string? Predicate { get; set; }

        public bool IsValid { get; set; }

        public bool IsReady { get; set; }

        public short KeyCount { get; set; }
    }
}
