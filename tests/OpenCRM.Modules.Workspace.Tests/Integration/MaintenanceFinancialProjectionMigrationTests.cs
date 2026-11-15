using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class MaintenanceFinancialProjectionMigrationTests
{
    [Fact]
    public async Task ProjectionMigrationRepairsDerivedRowsAndPreservesSourceAcrossRollbackAndReapplication()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(postgres.GetConnectionString(), database =>
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name)).Options;
        await using var context = new OpenCRMContext(options);
        var migrator = context.GetService<IMigrator>();
        var previous = context.Database.GetMigrations().Single(name => name.EndsWith("_AddMaintenanceFinancialTotals"));
        var migration = context.Database.GetMigrations().Single(name => name.EndsWith("_ExpandMaintenanceFinancialProjection"));
        await migrator.MigrateAsync(previous);
        var user = new ApplicationUser();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity."AspNetUsers"
                ("Id", "DatetimeCreation", "LastDateOfEntry", "CountVisits", "LastLoginDate", "CountLogins",
                 "IsDeleted", "CanUseTestSubscriptions", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled",
                 "LockoutEnabled", "AccessFailedCount", "PersonalDataProcessingConsentAccepted", "AdvertisingConsentAccepted")
            VALUES ({user.Id}, now(), now(), 0, now(), 0, false, false, false, false, false, false, 0, false, false)
            """);
        var organization = new Organization { Name = "Миграция финансовой проекции", OwnerUserId = user.Id };
        await HistoricalMigrationSeed.InsertOrganizationAsync(context, organization);
        var buyerId = Guid.NewGuid();
        var buyerName = "Текущее имя клиента";
        var vehicle = new Vehicle
        {
            UserId = user.Id,
            Organization = organization,
            ManualBrandName = "BMW",
            ManualModelName = "X3"
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "OrganizationClients" ("Id", "OrganizationId", "Name", "Phone", "Comment", "IsDeleted", "DeletionDate", "CreatedAt", "UpdatedAt")
            VALUES ({buyerId}, {organization.Id}, {buyerName}, NULL, NULL, false, NULL, now(), now())
            """);
        context.ChangeTracker.Clear();
        var firstId = Guid.NewGuid();
        var emptyId = Guid.NewGuid();
        var date = new DateTime(2026, 10, 19, 10, 20, 30, DateTimeKind.Utc);
        var planned = date.AddDays(1);
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "Maintenance" ("Id", "Type", "Status", "Date", "DateTimeCreation", "CompletedDate", "PlannedDate",
                "IsDeleted", "UserId", "VehicleId", "BuyerClientId", "BuyerName", "DiscountAmount", "PaidAmount", "MaterialCostAmount", "LaborCostAmount")
            VALUES ({firstId}, 0, 1, {date.AddDays(-1)}, {date}, {date}, {planned}, false, {user.Id}, {vehicle.Id}, {buyerId}, ' Клиент ', 3, 5, 2, 1),
                ({emptyId}, 0, 1, {date}, {date}, NULL, {planned}, true, {user.Id}, {vehicle.Id}, NULL, 'Без связи', 9, -1, 0, 0);
            INSERT INTO "MaintenanceDetails" ("MaintenanceId", "DetailId", "Position", "Name", "Price", "Quantity")
            VALUES ({firstId}, {Guid.NewGuid()}, 0, 'Первая работа', 25, 1),
                ({firstId}, {Guid.NewGuid()}, 1, 'Отрицательная цена', -5, 1),
                ({firstId}, {Guid.NewGuid()}, 2, 'Без цены', NULL, 1);
            INSERT INTO "MaintenanceFinancialTotals" ("MaintenanceId", "GrossAmount") VALUES ({firstId}, 999);
            """);
        var original = await MaintenanceFinancialProjectionMigrationTests.ReadSourceAsync(context);
        var originalTriggers = await MaintenanceFinancialProjectionMigrationTests.ReadTriggersAsync(context);
        foreach (var target in new[] { migration, previous, migration })
        {
            await migrator.MigrateAsync(target);
            Assert.Equal(original, await MaintenanceFinancialProjectionMigrationTests.ReadSourceAsync(context));
            Assert.Equal(originalTriggers, await MaintenanceFinancialProjectionMigrationTests.ReadTriggersAsync(context));
            var gross = await context.Database.SqlQuery<decimal>($"""
                SELECT "GrossAmount" AS "Value" FROM "MaintenanceFinancialTotals" WHERE "MaintenanceId" = {firstId}
                """).SingleAsync();
            var emptyGross = await context.Database.SqlQuery<decimal>($"""
                SELECT "GrossAmount" AS "Value" FROM "MaintenanceFinancialTotals" WHERE "MaintenanceId" = {emptyId}
                """).SingleAsync();
            Assert.Equal(20m, gross);
            Assert.Equal(0m, emptyGross);
            if (target == previous)
            {
                Assert.False(await context.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'MaintenanceFinancialTotals' AND column_name = 'NetAmount') AS "Value"
                    """).SingleAsync());

                continue;
            }

            var rows = await context.MaintenanceFinancialTotals.AsNoTracking()
                .Select(row => new
                {
                    row.MaintenanceId,
                    row.OrganizationId,
                    row.EffectiveDate,
                    row.UtcDay,
                    row.IsVisible,
                    row.ClientId,
                    row.ClientName,
                    row.NetAmount,
                    row.PaidAmount,
                    row.DebtAmount,
                    row.DiscountAmount,
                    row.MaterialCostAmount,
                    row.LaborCostAmount,
                    row.ProfitAmount,
                    row.FirstKey
                })
                .ToDictionaryAsync(row => row.MaintenanceId);
            Assert.Equal(2, rows.Count);
            var first = rows[firstId];
            Assert.Equal(organization.Id, first.OrganizationId);
            Assert.Equal(date, first.EffectiveDate);
            Assert.Equal((int)(date.Ticks / TimeSpan.TicksPerDay), first.UtcDay);
            Assert.True(first.IsVisible);
            Assert.Equal(buyerId, first.ClientId);
            Assert.Equal(" Клиент ", first.ClientName);
            Assert.Equal(17m, first.NetAmount);
            Assert.Equal(5m, first.PaidAmount);
            Assert.Equal(12m, first.DebtAmount);
            Assert.Equal(3m, first.DiscountAmount);
            Assert.Equal(2m, first.MaterialCostAmount);
            Assert.Equal(1m, first.LaborCostAmount);
            Assert.Equal(14m, first.ProfitAmount);
            Assert.Equal((315537897599999999L - date.Ticks / 10).ToString("D18", CultureInfo.InvariantCulture)
                + ":" + firstId.ToString("D"), first.FirstKey);
            var empty = rows[emptyId];
            Assert.Equal(organization.Id, empty.OrganizationId);
            Assert.Equal(planned, empty.EffectiveDate);
            Assert.Equal((int)(planned.Ticks / TimeSpan.TicksPerDay), empty.UtcDay);
            Assert.False(empty.IsVisible);
            Assert.Null(empty.ClientId);
            Assert.Null(empty.ClientName);
            Assert.Equal(0m, empty.NetAmount);
            Assert.Equal(-1m, empty.PaidAmount);
            Assert.Equal(1m, empty.DebtAmount);
            Assert.Equal(9m, empty.DiscountAmount);
            Assert.Equal(0m, empty.MaterialCostAmount);
            Assert.Equal(0m, empty.LaborCostAmount);
            Assert.Equal(0m, empty.ProfitAmount);
            Assert.Equal((315537897599999999L - planned.Ticks / 10).ToString("D18", CultureInfo.InvariantCulture)
                + ":" + emptyId.ToString("D"), empty.FirstKey);
            await MaintenanceFinancialProjectionMigrationTests.AssertMetadataAsync(context);
        }
    }

    private static Task<string> ReadSourceAsync(OpenCRMContext context) => context.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object(
            'users', (SELECT jsonb_agg(to_jsonb(row) || jsonb_build_object('xmin', row.xmin::text) ORDER BY "Id") FROM identity."AspNetUsers" AS row),
            'orders', (SELECT jsonb_agg(to_jsonb(row) || jsonb_build_object('xmin', row.xmin::text) ORDER BY "Id") FROM "Maintenance" AS row),
            'details', (SELECT jsonb_agg(to_jsonb(row) || jsonb_build_object('xmin', row.xmin::text) ORDER BY "MaintenanceId", "Position") FROM "MaintenanceDetails" AS row),
            'vehicles', (SELECT jsonb_agg(to_jsonb(row) || jsonb_build_object('xmin', row.xmin::text) ORDER BY "Id") FROM "Vehicles" AS row),
            'clients', (SELECT jsonb_agg(to_jsonb(row) || jsonb_build_object('xmin', row.xmin::text) ORDER BY "Id") FROM "OrganizationClients" AS row),
            'organizations', (SELECT jsonb_agg(to_jsonb(row) || jsonb_build_object('xmin', row.xmin::text) ORDER BY "Id") FROM "Organizations" AS row)
        )::text AS "Value"
        """).SingleAsync();

    private static async Task AssertMetadataAsync(OpenCRMContext context)
    {
        var indexes = await context.Database.SqlQuery<ProjectionIndex>($"""
            SELECT relation.relname AS "Name", metadata.indisvalid AS "IsValid", metadata.indisready AS "IsReady",
                pg_get_indexdef(metadata.indexrelid) AS "Definition", pg_get_expr(metadata.indpred, metadata.indrelid) AS "Predicate"
            FROM pg_index AS metadata JOIN pg_class AS relation ON relation.oid = metadata.indexrelid
            WHERE relation.relname IN ('IX_MaintenanceFinancialTotals_FinanceDate',
                'IX_MaintenanceFinancialTotals_DebtDate', 'IX_MaintenanceFinancialTotals_OrderDay')
            """).ToListAsync();
        Assert.Equal(3, indexes.Count);
        Assert.All(indexes, index =>
        {
            Assert.True(index.IsValid);
            Assert.True(index.IsReady);
            Assert.Contains("INCLUDE", index.Definition);
            Assert.Contains("IsVisible", index.Predicate);
        });
        Assert.True(await context.Database.SqlQuery<bool>($"""
            SELECT convalidated AND confdeltype = 'n' AS "Value" FROM pg_constraint
            WHERE conname = 'FK_MaintenanceFinancialTotals_Organizations_OrganizationId'
            """).SingleAsync());
    }

    private static Task<string> ReadTriggersAsync(OpenCRMContext context) => context.Database.SqlQuery<string>($"""
        SELECT COALESCE(jsonb_agg(pg_get_triggerdef(oid) ORDER BY tgrelid, tgname), '[]'::jsonb)::text AS "Value"
        FROM pg_trigger WHERE NOT tgisinternal
            AND tgrelid IN ('"MaintenanceFinancialTotals"'::regclass, '"Maintenance"'::regclass,
                '"MaintenanceDetails"'::regclass)
        """).SingleAsync();

    private sealed class ProjectionIndex
    {
        public string Name { get; set; } = "";

        public bool IsValid { get; set; }

        public bool IsReady { get; set; }

        public string Definition { get; set; } = "";

        public string Predicate { get; set; } = "";
    }
}
