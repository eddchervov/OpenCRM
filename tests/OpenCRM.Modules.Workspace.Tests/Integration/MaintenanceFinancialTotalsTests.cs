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

public sealed class MaintenanceFinancialTotalsTests
{
    [Fact]
    public async Task TotalsSurviveMigrationRollbackAndTrackTransactionalConcurrentChanges()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await postgres.StartAsync();
        var connectionString = postgres.GetConnectionString();
        await using var context = MaintenanceFinancialTotalsTests.CreateContext(connectionString);
        var migrator = context.GetService<IMigrator>();
        const string previous = "20261019110312_IndexWorkspaceReportingDates";
        await migrator.MigrateAsync(previous);
        var user = new ApplicationUser { Email = "financial-totals@example.test" };
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity."AspNetUsers"
                ("Id", "Email", "NormalizedEmail", "DatetimeCreation", "LastDateOfEntry", "CountVisits",
                 "LastLoginDate", "CountLogins", "IsDeleted", "CanUseTestSubscriptions", "EmailConfirmed",
                 "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount",
                 "PersonalDataProcessingConsentAccepted", "AdvertisingConsentAccepted")
            VALUES ({user.Id}, {user.Email}, {user.Email!.ToUpperInvariant()}, now(), now(), 0, now(), 0, false,
                    false, false, false, false, false, 0, false, false)
            """);
        var vehicle = new Vehicle
        {
            UserId = user.Id,
            ManualBrandName = "BMW",
            ManualModelName = "X3"
        };
        var date = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);
        var first = new OpenCRM.Domain.Maintenance
        {
            UserId = user.Id, Vehicle = vehicle, Date = date, DateTimeCreation = date
        };
        var second = new OpenCRM.Domain.Maintenance
        {
            UserId = user.Id, Vehicle = vehicle, Date = date, DateTimeCreation = date
        };
        await HistoricalMigrationSeed.InsertVehicleAsync(context, vehicle);
        context.ChangeTracker.Clear();

        // Исторические заказы создаются до появления производных финансовых итогов.
        foreach (var order in new[] { first, second })
        {
            await context.Database.ExecuteSqlAsync($"""
                INSERT INTO "Maintenance" ("Id", "Type", "Date", "DateTimeCreation", "IsDeleted", "UserId", "VehicleId", "Status")
                VALUES ({order.Id}, 0, {order.Date}, {order.DateTimeCreation}, {order.IsDeleted}, {user.Id}, {vehicle.Id}, {(int)order.Status});
                """);
        }

        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "MaintenanceDetails" ("MaintenanceId", "DetailId", "Position", "Name", "Price", "Quantity")
            VALUES ({first.Id}, {Guid.NewGuid()}, 0, 'Первая', 0.10, 1),
                ({first.Id}, {Guid.NewGuid()}, 1, 'Вторая', 12.34, 1),
                ({first.Id}, {Guid.NewGuid()}, 2, 'Без цены', NULL, 1)
            """);
        var migration = context.Database.GetMigrations().Single(name => name.EndsWith("_AddMaintenanceFinancialTotals"));
        var version = await context.Database.SqlQuery<string>(
            $"""SELECT xmin::text AS "Value" FROM "Maintenance" WHERE "Id" = {first.Id}""").SingleAsync();

        foreach (var target in new[] { migration, previous, migration })
        {
            await migrator.MigrateAsync(target);
            Assert.Equal(12.44m, await context.MaintenanceDetails.SumAsync(detail => detail.Price) ?? 0m);
            Assert.Equal(3, await context.MaintenanceDetails.CountAsync());
            if (target == migration)
            {
                Assert.Equal(12.44m, await MaintenanceFinancialTotalsTests.ReadTotalAsync(context, first.Id));
            }
        }

        await migrator.MigrateAsync();
        var index = await context.Database.SqlQuery<string>(
            $"""SELECT indexdef AS "Value" FROM pg_indexes WHERE indexname = 'IX_MaintenanceDetails_MaintenanceId_Position'""").SingleAsync();
        Assert.Contains("INCLUDE (\"Price\", \"DetailId\")", index);
        var foreignKey = await context.Database.SqlQuery<bool>(
            $"""SELECT convalidated AND confdeltype = 'c' AS "Value" FROM pg_constraint WHERE conname = 'FK_MaintenanceFinancialTotals_Maintenance_MaintenanceId'""").SingleAsync();
        Assert.True(foreignKey);

        Assert.False(await context.Database.SqlQuery<bool>(
            $"""
            SELECT EXISTS (SELECT 1 FROM pg_trigger
                WHERE tgrelid = '"MaintenanceDetails"'::regclass AND tgname = 'TR_MaintenanceDetails_FinancialTotal') AS "Value"
            """).SingleAsync());
        var changed = await context.MaintenanceDetails.SingleAsync(detail => detail.MaintenanceId == first.Id && detail.Position == 0);
        changed.Price = -2.50m;
        await context.SaveChangesAsync();
        Assert.Equal(9.84m, await MaintenanceFinancialTotalsTests.ReadTotalAsync(context, first.Id));
        var moved = await context.MaintenanceDetails.SingleAsync(detail => detail.MaintenanceId == first.Id && detail.Position == 1);
        context.MaintenanceDetails.Remove(moved);
        context.MaintenanceDetails.Add(new MaintenanceDetail
        {
            MaintenanceId = second.Id, DetailId = moved.DetailId, Position = moved.Position,
            Name = moved.Name, Price = moved.Price
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(-2.50m, await MaintenanceFinancialTotalsTests.ReadTotalAsync(context, first.Id));
        Assert.Equal(12.34m, await MaintenanceFinancialTotalsTests.ReadTotalAsync(context, second.Id));
        var unpriced = await context.MaintenanceDetails.SingleAsync(detail => detail.MaintenanceId == second.Id);
        unpriced.Price = null;
        await context.SaveChangesAsync();
        Assert.Equal(0m, await MaintenanceFinancialTotalsTests.ReadTotalAsync(context, second.Id));
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var rolledBack = await context.MaintenanceDetails.Where(detail => detail.MaintenanceId == first.Id).ToArrayAsync();
            foreach (var detail in rolledBack)
            {
                detail.Price = 99m;
            }
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        Assert.Equal(-2.50m, await MaintenanceFinancialTotalsTests.ReadTotalAsync(context, first.Id));

        var longName = new string(Enumerable.Range(0, 1000).Select(index => (char)(0x4E00 + index)).ToArray());
        context.MaintenanceDetails.Add(new MaintenanceDetail
        {
            MaintenanceId = second.Id, Name = longName, Position = 99, Price = 3.21m
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(longName, await context.MaintenanceDetails.Where(detail => detail.MaintenanceId == second.Id && detail.Position == 99)
            .Select(detail => detail.Name).SingleAsync());
        Assert.Equal(3.21m, await MaintenanceFinancialTotalsTests.ReadTotalAsync(context, second.Id));

        var imported = Enumerable.Range(3, 20).Select(position => new MaintenanceDetail
        {
            MaintenanceId = first.Id, Name = "Импорт", Position = position, Price = 0.01m
        }).ToArray();
        context.MaintenanceDetails.AddRange(imported);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(-2.30m, await MaintenanceFinancialTotalsTests.ReadTotalAsync(context, first.Id));
        await using var concurrent = MaintenanceFinancialTotalsTests.CreateContext(connectionString);
        var firstConcurrent = await context.MaintenanceDetails.SingleAsync(detail => detail.MaintenanceId == first.Id && detail.Position == 3);
        var secondConcurrent = await concurrent.MaintenanceDetails.SingleAsync(detail => detail.MaintenanceId == first.Id && detail.Position == 4);
        firstConcurrent.Price = 4m;
        secondConcurrent.Price = 7m;
        await Task.WhenAll(context.SaveChangesAsync(), concurrent.SaveChangesAsync());
        context.ChangeTracker.Clear();
        Assert.Equal(8.68m, await MaintenanceFinancialTotalsTests.ReadTotalAsync(context, first.Id));
        Assert.Equal(version, await context.Database.SqlQuery<string>(
            $"""SELECT xmin::text AS "Value" FROM "Maintenance" WHERE "Id" = {first.Id}""").SingleAsync());
        var totalVersion = await context.Database.SqlQuery<string>(
            $"""SELECT xmin::text AS "Value" FROM "MaintenanceFinancialTotals" WHERE "MaintenanceId" = {first.Id}""").SingleAsync();
        var renamed = await context.MaintenanceDetails.Where(detail => detail.MaintenanceId == first.Id).ToArrayAsync();
        foreach (var detail in renamed)
        {
            detail.Name = "Переименована";
            detail.Position += 100;
        }
        await context.SaveChangesAsync();
        Assert.Equal(totalVersion, await context.Database.SqlQuery<string>(
            $"""SELECT xmin::text AS "Value" FROM "MaintenanceFinancialTotals" WHERE "MaintenanceId" = {first.Id}""").SingleAsync());
        context.MaintenanceDetails.RemoveRange(renamed);
        await context.SaveChangesAsync();
        Assert.Equal(0m, await MaintenanceFinancialTotalsTests.ReadTotalAsync(context, first.Id));
        context.ChangeTracker.Clear();
        context.Maintenance.Remove(await context.Maintenance.SingleAsync(order => order.Id == second.Id));
        await context.SaveChangesAsync();
        Assert.False(await context.MaintenanceFinancialTotals.AnyAsync(total => total.MaintenanceId == second.Id));
        Assert.False(await context.MaintenanceDetails.AnyAsync(detail => detail.MaintenanceId == second.Id));
        await using var retrying = MaintenanceFinancialTotalsTests.CreateContext(connectionString, true);
        var synchronous = new MaintenanceDetail { MaintenanceId = first.Id, Name = "Синхронная", Position = 0, Price = 2m };
        retrying.MaintenanceDetails.Add(synchronous);
        retrying.SaveChanges(false);
        Assert.Equal(EntityState.Added, retrying.Entry(synchronous).State);
        Assert.Equal(2m, await MaintenanceFinancialTotalsTests.ReadTotalAsync(context, first.Id));
        retrying.ChangeTracker.AcceptAllChanges();
        retrying.MaintenanceDetails.Remove(synchronous);
        retrying.SaveChanges();
        Assert.Equal(0m, await MaintenanceFinancialTotalsTests.ReadTotalAsync(context, first.Id));

        await context.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "MaintenanceFinancialTotals" ADD CONSTRAINT "CK_Test_TotalLimit" CHECK ("GrossAmount" <= 5)""");
        var failing = new MaintenanceDetail { MaintenanceId = first.Id, Name = "Откат", Position = 0, Price = 6m };
        context.MaintenanceDetails.Add(failing);
        var ownFailure = await Assert.ThrowsAsync<PostgresException>(() => context.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, ownFailure.SqlState);
        Assert.False(await context.MaintenanceDetails.AnyAsync(detail => detail.MaintenanceId == first.Id));
        Assert.Equal(0m, await MaintenanceFinancialTotalsTests.ReadTotalAsync(context, first.Id));
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => context.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
            context.ChangeTracker.Clear();
            await transaction.CommitAsync();
        }
        Assert.False(await context.MaintenanceDetails.AnyAsync(detail => detail.MaintenanceId == first.Id));
        Assert.Equal(0m, await MaintenanceFinancialTotalsTests.ReadTotalAsync(context, first.Id));
    }

    private static OpenCRMContext CreateContext(string connectionString, bool enableRetry = false)
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(connectionString, database =>
                {
                    database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
                    if (enableRetry)
                    {
                        database.EnableRetryOnFailure();
                    }
                })
            .Options;

        return new OpenCRMContext(options);
    }

    private static Task<decimal> ReadTotalAsync(OpenCRMContext context, Guid maintenanceId) =>
        context.MaintenanceFinancialTotals.Where(total => total.MaintenanceId == maintenanceId)
            .Select(total => total.GrossAmount).SingleAsync();
}
