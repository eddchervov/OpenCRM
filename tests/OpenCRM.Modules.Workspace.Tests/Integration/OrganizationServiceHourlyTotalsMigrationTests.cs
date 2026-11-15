using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

public sealed class OrganizationServiceHourlyTotalsMigrationTests
{
    [Fact]
    public async Task AccrualMigrationExcludesCancelledOrdersAndDownRestoresPreviousProjectionAndHourlyTotals()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(postgres.GetConnectionString(), database =>
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name)).Options;
        await using var context = new OpenCRMContext(options);
        var migrator = context.GetService<IMigrator>();
        var previous = context.Database.GetMigrations().Single(name => name.EndsWith("_ExpandOrganizationAuditDescription"));
        var migration = context.Database.GetMigrations().Single(name => name.EndsWith("_AddMaintenanceAccrualFlag"));
        await migrator.MigrateAsync(previous);
        var user = new ApplicationUser();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity."AspNetUsers"
                ("Id", "DatetimeCreation", "LastDateOfEntry", "CountVisits", "LastLoginDate", "CountLogins",
                 "IsDeleted", "CanUseTestSubscriptions", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled",
                 "LockoutEnabled", "AccessFailedCount", "PersonalDataProcessingConsentAccepted", "AdvertisingConsentAccepted")
            VALUES ({user.Id}, now(), now(), 0, now(), 0, false, false, false, false, false, false, 0, false, false)
            """);
        var organization = new Organization { Name = "Начисления отменённых заказов", OwnerUserId = user.Id };
        await HistoricalMigrationSeed.InsertOrganizationAsync(context, organization);
        var vehicle = new Vehicle { UserId = user.Id, Organization = organization, ManualBrandName = "BMW", ManualModelName = "X3" };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        var date = new DateTime(2026, 10, 19, 10, 20, 30, DateTimeKind.Utc);
        await OrganizationServiceHourlyTotalsMigrationTests.SeedOrderAsync(context, vehicle, date, false,
            [new("Полировка", 3000m, 0)], status: 4, discount: 200m, paid: 500m, materialCost: 120m, laborCost: 80m);

        await migrator.MigrateAsync(migration);
        var accrued = await context.MaintenanceFinancialTotals.AsNoTracking().SingleAsync();
        Assert.True(accrued.IsVisible);
        Assert.False(accrued.IsAccruable);
        Assert.Equal(3000m, accrued.GrossAmount);
        Assert.Equal(200m, accrued.DiscountAmount);
        Assert.Equal(0m, accrued.NetAmount);
        Assert.Equal(500m, accrued.PaidAmount);
        Assert.Equal(0m, accrued.DebtAmount);
        Assert.Equal(-200m, accrued.ProfitAmount);
        Assert.Empty(await context.OrganizationServiceHourlyTotals.AsNoTracking().ToListAsync());

        await migrator.MigrateAsync(previous);
        Assert.Equal(2800m, await context.Database.SqlQuery<decimal>($"""
            SELECT "NetAmount" AS "Value" FROM "MaintenanceFinancialTotals"
            """).SingleAsync());
        Assert.Equal(2300m, await context.Database.SqlQuery<decimal>($"""
            SELECT "DebtAmount" AS "Value" FROM "MaintenanceFinancialTotals"
            """).SingleAsync());
        Assert.Equal(2600m, await context.Database.SqlQuery<decimal>($"""
            SELECT "ProfitAmount" AS "Value" FROM "MaintenanceFinancialTotals"
            """).SingleAsync());
        Assert.Equal(1, await context.Database.SqlQuery<int>($"""
            SELECT COUNT(*)::integer AS "Value" FROM "OrganizationServiceHourlyTotals"
            """).SingleAsync());

        await migrator.MigrateAsync(migration);
        var reapplied = await context.MaintenanceFinancialTotals.AsNoTracking().SingleAsync();
        Assert.False(reapplied.IsAccruable);
        Assert.Equal(0m, reapplied.NetAmount);
        Assert.Equal(0m, reapplied.DebtAmount);
        Assert.Equal(500m, reapplied.PaidAmount);
        Assert.Empty(await context.OrganizationServiceHourlyTotals.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task HourlyMigrationBackfillsExactGroupsAndPreservesSourceAcrossRollbackAndReapplication()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(postgres.GetConnectionString(), database =>
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name)).Options;
        await using var context = new OpenCRMContext(options);
        var migrator = context.GetService<IMigrator>();
        var previous = context.Database.GetMigrations().Single(name => name.EndsWith("_ExpandMaintenanceFinancialProjection"));
        var migration = context.Database.GetMigrations().Single(name => name.EndsWith("_PersistOrganizationServiceHourlyTotals"));
        await migrator.MigrateAsync(previous);
        var user = new ApplicationUser();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity."AspNetUsers"
                ("Id", "DatetimeCreation", "LastDateOfEntry", "CountVisits", "LastLoginDate", "CountLogins",
                 "IsDeleted", "CanUseTestSubscriptions", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled",
                 "LockoutEnabled", "AccessFailedCount", "PersonalDataProcessingConsentAccepted", "AdvertisingConsentAccepted")
            VALUES ({user.Id}, now(), now(), 0, now(), 0, false, false, false, false, false, false, 0, false, false)
            """);
        var organization = new Organization { Name = "Миграция почасовых услуг", OwnerUserId = user.Id };
        await HistoricalMigrationSeed.InsertOrganizationAsync(context, organization);
        var vehicle = new Vehicle { UserId = user.Id, Organization = organization, ManualBrandName = "BMW", ManualModelName = "X3" };
        var personal = new Vehicle { UserId = user.Id, ManualBrandName = "BMW", ManualModelName = "X3" };
        var deleted = new Vehicle
        {
            UserId = user.Id, Organization = organization, ManualBrandName = "BMW", ManualModelName = "X3", IsDeleted = true
        };
        context.Vehicles.AddRange(vehicle, personal, deleted);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var date = new DateTime(2026, 10, 19, 10, 20, 30, DateTimeKind.Utc);
        var longName = new string(Enumerable.Range(0, 1000).Select(index => (char)(0x4E00 + index)).ToArray());
        await OrganizationServiceHourlyTotalsMigrationTests.SeedOrderAsync(context, vehicle, date, false,
            [new("Полировка", 11m, 3), new("Полировка", 7m, 1), new("полировка", 5m, 2),
                new(" Полировка\u00A0", 4m, 4), new("\u2003", 3m, 5), new(longName, 12.34m, 6),
                new("Нулевая", 0m, 7), new("Отрицательная", -2m, 8), new("Без цены", null, 9)]);
        await OrganizationServiceHourlyTotalsMigrationTests.SeedOrderAsync(context, vehicle, date.AddMinutes(10), false,
            [new("Полировка", 13m, 0), new("Полировка", 17m, 2)]);
        await OrganizationServiceHourlyTotalsMigrationTests.SeedOrderAsync(context, vehicle, date.AddHours(1), false,
            [new("Полировка", 19m, 0), new(longName, 2.66m, 1)]);
        await OrganizationServiceHourlyTotalsMigrationTests.SeedOrderAsync(context, vehicle, date, true,
            [new("Удалённый заказ", 100m, 0)]);
        await OrganizationServiceHourlyTotalsMigrationTests.SeedOrderAsync(context, personal, date, false,
            [new("Личная техника", 100m, 0)]);
        await OrganizationServiceHourlyTotalsMigrationTests.SeedOrderAsync(context, deleted, date, false,
            [new("Удалённая техника", 100m, 0)]);
        var minimum = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        var maximum = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        await OrganizationServiceHourlyTotalsMigrationTests.SeedOrderAsync(context, vehicle, minimum, false,
            [new("Крайняя дата", 2m, 0)]);
        await OrganizationServiceHourlyTotalsMigrationTests.SeedOrderAsync(context, vehicle, minimum.AddTicks(10), false,
            [new("Крайняя дата", 3m, 1)]);
        await OrganizationServiceHourlyTotalsMigrationTests.SeedOrderAsync(context, vehicle, maximum, false,
            [new("Крайняя дата", 5m, 0)]);
        await OrganizationServiceHourlyTotalsMigrationTests.SeedOrderAsync(context, vehicle, maximum.AddTicks(-9), false,
            [new("Крайняя дата", 7m, 1)]);
        var source = await OrganizationServiceHourlyTotalsMigrationTests.ReadSourceAsync(context);
        var triggers = await OrganizationServiceHourlyTotalsMigrationTests.ReadTriggersAsync(context);
        foreach (var target in new[] { migration, previous, migration })
        {
            await migrator.MigrateAsync(target);
            Assert.Equal(source, await OrganizationServiceHourlyTotalsMigrationTests.ReadSourceAsync(context));
            Assert.Equal(triggers, await OrganizationServiceHourlyTotalsMigrationTests.ReadTriggersAsync(context));
            if (target == previous)
            {
                Assert.False(await context.Database.SqlQuery<bool>($"""
                    SELECT to_regclass('"OrganizationServiceHourlyTotals"') IS NOT NULL AS "Value"
                    """).SingleAsync());

                continue;
            }

            await OrganizationServiceHourlyTotalsMigrationTests.AssertSourceParityAsync(context);
            await OrganizationServiceHourlyTotalsMigrationTests.AssertMetadataAndConstraintsAsync(context, organization.Id);
            Assert.Equal(source, await OrganizationServiceHourlyTotalsMigrationTests.ReadSourceAsync(context));
        }
    }

    private static async Task SeedOrderAsync(
        OpenCRMContext context,
        Vehicle vehicle,
        DateTime date,
        bool isDeleted,
        SeedDetail[] details,
        int status = 1,
        decimal discount = 0m,
        decimal paid = 0m,
        decimal materialCost = 0m,
        decimal laborCost = 0m)
    {
        var orderId = Guid.NewGuid();
        var firstKey = (315537897599999999L - date.Ticks / 10).ToString("D18", CultureInfo.InvariantCulture)
            + ":" + orderId.ToString("D");
        var gross = details.Sum(detail => detail.Price ?? 0m);
        var net = Math.Max(0m, gross - discount);
        var visible = !isDeleted && !vehicle.IsDeleted && vehicle.OrganizationId.HasValue;
        var utcDay = (int)(date.Ticks / TimeSpan.TicksPerDay);
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO "Maintenance" ("Id", "Type", "Status", "Date", "CompletedDate", "DateTimeCreation",
                "IsDeleted", "UserId", "VehicleId", "DiscountAmount", "PaidAmount", "MaterialCostAmount", "LaborCostAmount")
            VALUES ({orderId}, 0, {status}, {date}, {date}, {date}, {isDeleted}, {vehicle.UserId}, {vehicle.Id},
                {discount}, {paid}, {materialCost}, {laborCost});
            INSERT INTO "MaintenanceFinancialTotals" ("MaintenanceId", "GrossAmount", "OrganizationId", "EffectiveDate",
                "UtcDay", "IsVisible", "FirstKey", "NetAmount", "PaidAmount", "DebtAmount", "DiscountAmount",
                "MaterialCostAmount", "LaborCostAmount", "ProfitAmount")
            VALUES ({orderId}, {gross}, {vehicle.OrganizationId}, {date}, {utcDay}, {visible}, {firstKey}, {net}, {paid},
                GREATEST(0, {net} - {paid}), {discount}, {materialCost}, {laborCost}, {net} - {materialCost} - {laborCost});
            """);
        foreach (var detail in details)
        {
            await context.Database.ExecuteSqlAsync($"""
                INSERT INTO "MaintenanceDetails" ("MaintenanceId", "DetailId", "Position", "Name", "Price", "Quantity")
                VALUES ({orderId}, {Guid.NewGuid()}, {detail.Position}, {detail.Name}, {detail.Price}, 1)
                """);
        }
    }

    private static async Task AssertSourceParityAsync(OpenCRMContext context)
    {
        var orders = await context.Maintenance.AsNoTracking().IgnoreQueryFilters()
            .Select(order => new
            {
                order.Id,
                order.IsDeleted,
                order.Date,
                order.CompletedDate,
                order.PlannedDate,
                Vehicle = new { order.Vehicle.IsDeleted, order.Vehicle.OrganizationId },
                Details = order.Details.Select(detail => new
                {
                    detail.DetailId,
                    detail.Position,
                    detail.Name,
                    detail.Price
                }).ToList()
            })
            .ToListAsync();
        var expected = orders.Where(order => !order.IsDeleted && !order.Vehicle.IsDeleted && order.Vehicle.OrganizationId.HasValue)
            .SelectMany(order => order.Details.Where(detail => detail.Price > 0 && detail.Name != null).Select(detail => new
            {
                OrganizationId = order.Vehicle.OrganizationId!.Value,
                UtcHour = (int)((order.CompletedDate ?? order.PlannedDate ?? order.Date).Ticks / TimeSpan.TicksPerHour),
                detail.Name,
                Amount = detail.Price!.Value,
                FirstKey = (315537897599999999L - (order.CompletedDate ?? order.PlannedDate ?? order.Date).Ticks / 10)
                    .ToString("D18", CultureInfo.InvariantCulture) + ":" + order.Id.ToString("D") + ":"
                    + detail.Position.ToString("D10", CultureInfo.InvariantCulture) + ":" + detail.DetailId.ToString("D")
            }))
            .GroupBy(detail => (detail.OrganizationId, detail.UtcHour, detail.Name)).ToDictionary(group => group.Key, group => new
            {
                Count = group.LongCount(), Amount = group.Sum(detail => detail.Amount),
                FirstKey = group.Select(detail => detail.FirstKey).Order(StringComparer.Ordinal).First()
            });
        var actual = await context.OrganizationServiceHourlyTotals.AsNoTracking().ToListAsync();
        Assert.Equal(expected.Count, actual.Count);
        Assert.Contains(actual, row => row.UtcHour == 0 && row.Count == 2 && row.Amount == 5m);
        Assert.Contains(actual, row => row.UtcHour == 87649415 && row.Count == 2 && row.Amount == 12m);
        Assert.Contains(actual, row => row.Name == "Полировка" && row.Count == 4 && row.Amount == 48m);
        foreach (var row in actual)
        {
            Assert.True(expected.TryGetValue((row.OrganizationId, row.UtcHour, row.Name), out var group));
            Assert.Equal(group.Count, row.Count);
            Assert.Equal(group.Amount, row.Amount);
            Assert.Equal(group.FirstKey, row.FirstKey);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(row.Name))), row.NameHash);
        }
    }

    private static async Task AssertMetadataAndConstraintsAsync(OpenCRMContext context, Guid organizationId)
    {
        var primaryKey = await context.Database.SqlQuery<string>($"""
            SELECT pg_get_constraintdef(oid) AS "Value" FROM pg_constraint
            WHERE conrelid = '"OrganizationServiceHourlyTotals"'::regclass AND contype = 'p'
            """).SingleAsync();
        Assert.Equal("PRIMARY KEY (\"OrganizationId\", \"UtcHour\", \"NameHash\")", primaryKey);
        Assert.True(await context.Database.SqlQuery<bool>($"""
            SELECT convalidated AND confdeltype = 'c' AS "Value" FROM pg_constraint
            WHERE conname = 'FK_OrganizationServiceHourlyTotals_Organizations_OrganizationId'
            """).SingleAsync());
        var hashCheck = await context.Database.SqlQuery<string>($"""
            SELECT pg_get_constraintdef(oid) AS "Value" FROM pg_constraint
            WHERE conname = 'CK_OrganizationServiceHourlyTotals_NameHash' AND convalidated
            """).SingleAsync();
        Assert.Contains("sha256", hashCheck);
        Assert.Contains("convert_to", hashCheck);
        var comment = await context.Database.SqlQuery<string>($"""
            SELECT obj_description('"OrganizationServiceHourlyTotals"'::regclass, 'pg_class') AS "Value"
            """).SingleAsync();
        Assert.Contains("C#", comment);
        Assert.Contains("без триггеров", comment);
        var collations = await context.Database.SqlQuery<string>($"""
            SELECT collation_name AS "Value" FROM information_schema.columns
            WHERE table_name = 'OrganizationServiceHourlyTotals' AND column_name IN ('Name', 'NameHash', 'FirstKey')
            """).ToArrayAsync();
        Assert.Equal(3, collations.Length);
        Assert.All(collations, collation => Assert.Equal("C", collation));
        var row = await context.OrganizationServiceHourlyTotals.AsNoTracking().FirstAsync();
        var violation = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlAsync($"""
            UPDATE "OrganizationServiceHourlyTotals" SET "NameHash" = {new string('0', 64)}
            WHERE "OrganizationId" = {row.OrganizationId} AND "UtcHour" = {row.UtcHour} AND "NameHash" = {row.NameHash}
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, violation.SqlState);
        await using var transaction = await context.Database.BeginTransactionAsync();
        await context.Database.ExecuteSqlAsync($"""DELETE FROM "Organizations" WHERE "Id" = {organizationId}""");
        Assert.False(await context.OrganizationServiceHourlyTotals.AnyAsync(total => total.OrganizationId == organizationId));
        await transaction.RollbackAsync();
    }

    private static Task<string> ReadSourceAsync(OpenCRMContext context) => context.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object(
            'orders', (SELECT jsonb_agg(to_jsonb(row) || jsonb_build_object('xmin', row.xmin::text) ORDER BY "Id") FROM "Maintenance" AS row),
            'details', (SELECT jsonb_agg(to_jsonb(row) || jsonb_build_object('xmin', row.xmin::text)
                ORDER BY "MaintenanceId", "Position", "DetailId") FROM "MaintenanceDetails" AS row),
            'vehicles', (SELECT jsonb_agg(to_jsonb(row) || jsonb_build_object('xmin', row.xmin::text) ORDER BY "Id") FROM "Vehicles" AS row),
            'organizations', (SELECT jsonb_agg(to_jsonb(row) || jsonb_build_object('xmin', row.xmin::text) ORDER BY "Id") FROM "Organizations" AS row),
            'financialTotals', (SELECT jsonb_agg(to_jsonb(row) || jsonb_build_object('xmin', row.xmin::text)
                ORDER BY "MaintenanceId") FROM "MaintenanceFinancialTotals" AS row)
        )::text AS "Value"
        """).SingleAsync();

    private static Task<string> ReadTriggersAsync(OpenCRMContext context) => context.Database.SqlQuery<string>($"""
        SELECT COALESCE(jsonb_agg(pg_get_triggerdef(oid) ORDER BY tgrelid, tgname), '[]'::jsonb)::text AS "Value"
        FROM pg_trigger WHERE NOT tgisinternal
            AND tgrelid IN ('"MaintenanceFinancialTotals"'::regclass, '"Maintenance"'::regclass,
                '"MaintenanceDetails"'::regclass, '"Vehicles"'::regclass, '"Organizations"'::regclass)
        """).SingleAsync();

    private sealed record SeedDetail(string Name, decimal? Price, int Position);
}
