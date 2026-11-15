using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class PaymentCreationMigrationTests
{
    [Fact]
    public async Task MigrationRejectsDuplicatePaymentIdsAndProtectsPersistedOperationKeysOnRollback()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await postgres.StartAsync();
        await using var context = new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(postgres.GetConnectionString(), database =>
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name))
            .Options);
        var migrator = context.GetService<IMigrator>();
        const string PreviousMigration = "20261009102414_RefreshRelationalMaintenanceAssignments";
        await migrator.MigrateAsync(PreviousMigration);
        var user = new ApplicationUser();
        var plan = new SubscriptionPlan { Name = "Проверка миграции оплаты", Price = 500, DurationDays = 30 };

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity."AspNetUsers"
                ("Id", "DatetimeCreation", "LastDateOfEntry", "CountVisits", "LastLoginDate", "CountLogins",
                 "IsDeleted", "CanUseTestSubscriptions", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled",
                 "LockoutEnabled", "AccessFailedCount", "PersonalDataProcessingConsentAccepted", "AdvertisingConsentAccepted")
            VALUES ({user.Id}, now(), now(), 0, now(), 0, false, false, false, false, false, false, 0, false, false)
            """);
        context.SubscriptionPlans.Add(plan);
        await context.SaveChangesAsync();
        var createdAt = DateTime.UtcNow;

        // До миграции используем историческую схему: в текущей модели уже есть новые столбцы.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SubscriptionPayments"
                ("UserId", "SubscriptionPlanId", "Amount", "CreatedAt", "ExternalPaymentId", "PaymentStatus", "PaymentMethod", "RefundedAmount", "BuyerTypeCode")
            VALUES
                ({user.Id}, {plan.Id}, {500m}, {createdAt}, {"duplicate-provider-id"}, {"completed"}, {string.Empty}, {0m}, {"unknown"}),
                ({user.Id}, {plan.Id}, {500m}, {createdAt}, {"duplicate-provider-id"}, {"completed"}, {string.Empty}, {0m}, {"unknown"});
            """);

        var duplicateError = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
        Assert.Equal("P0001", duplicateError.SqlState);
        Assert.Contains("ExternalPaymentId", duplicateError.MessageText);
        Assert.Equal(2, await context.SubscriptionPayments.CountAsync());
        Assert.DoesNotContain("AddPaymentCreationOperations", string.Join(',', await context.Database.GetAppliedMigrationsAsync()));

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "SubscriptionPayments"
            SET "ExternalPaymentId" = {"another-provider-id"}
            WHERE "Id" = (SELECT MAX("Id") FROM "SubscriptionPayments");
            """);
        await migrator.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(2, await context.SubscriptionPayments.CountAsync());

        await migrator.MigrateAsync(PreviousMigration);
        await migrator.MigrateAsync();
        context.PaymentCreationOperations.Add(new PaymentCreationOperation
        {
            IdempotenceKey = "persisted-key", UserId = user.Id, SubscriptionPlanId = plan.Id,
            OperationType = "yookassa_purchase", Amount = 500, Description = "Сохранённый запрос",
            BuyerTypeCode = "person", RequestTimestamp = DateTime.UtcNow,
            RequestBody = "{\"description\":\"Сохранённый запрос\"}", CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var rollbackError = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(PreviousMigration));
        Assert.Equal("P0001", rollbackError.SqlState);
        Assert.Contains("операции", rollbackError.MessageText);
        await migrator.MigrateAsync();
        Assert.Equal("persisted-key", await context.PaymentCreationOperations.Select(operation => operation.IdempotenceKey).SingleAsync());
        Assert.Equal(2, await context.SubscriptionPayments.CountAsync());
    }
}
