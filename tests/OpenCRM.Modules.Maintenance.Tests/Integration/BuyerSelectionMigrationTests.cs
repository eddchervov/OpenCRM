using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class BuyerSelectionMigrationTests(MaintenanceDatabaseFixture fixture)
    : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task RemovingFlagPreservesBuyerAndRollbackDoesNotRestoreOwnerFallback()
    {
        await using var context = fixture.CreateContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261011145029_AddPaymentCreationOperations");
        var userId = Guid.NewGuid().ToString();
        var userName = $"migration-{Guid.NewGuid():N}";
        var organizationId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var withoutBuyerId = Guid.NewGuid();
        var withBuyerId = Guid.NewGuid();

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity."AspNetUsers"
                ("Id", "UserName", "NormalizedUserName", "DatetimeCreation", "LastDateOfEntry", "CountVisits",
                 "LastLoginDate", "CountLogins", "IsDeleted", "CanUseTestSubscriptions", "EmailConfirmed",
                 "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount",
                 "PersonalDataProcessingConsentAccepted", "AdvertisingConsentAccepted")
            VALUES ({userId}, {userName}, {userName.ToUpperInvariant()}, now(), now(), 0, now(), 0, false, false,
                    false, false, false, false, 0, false, false)
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Organizations"
                ("Id", "Name", "PrimaryScenarioCode", "OwnerUserId", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES ({organizationId}, 'Проверка миграции', 'service_station', {userId}, true, now(), now())
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "OrganizationClients"
                ("Id", "OrganizationId", "Name", "PartyTypeCode", "StatusCode", "IsDeleted", "CreatedAt", "UpdatedAt",
                 "MaxNotificationsEnabled", "TelegramNotificationsEnabled")
            VALUES ({buyerId}, {organizationId}, 'Покупатель', 'person', 'active', false, now(), now(), false, false)
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Vehicles"
                ("Id", "Kind", "MileageUnit", "DateTimeCreation", "IsDeleted", "IsArchived", "UserId",
                 "OrganizationId", "OrganizationClientId")
            VALUES ({vehicleId}, 0, 1, now(), false, false, {userId}, {organizationId}, {buyerId})
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Maintenance"
                ("Id", "Status", "Type", "Date", "DateTimeCreation", "UserId", "VehicleId", "IsDeleted",
                 "DiscountAmount", "PaidAmount", "MaterialCostAmount", "LaborCostAmount", "BuyerClientId",
                 "IsBuyerSelectionRecorded")
            VALUES
                ({withoutBuyerId}, 3, 2, now(), now(), {userId}, {vehicleId}, false, 0, 0, 0, 0, NULL, false),
                ({withBuyerId}, 3, 2, now(), now(), {userId}, {vehicleId}, false, 0, 0, 0, 0, {buyerId}, false)
            """);

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();

        Assert.Null((await context.Maintenance.SingleAsync(order => order.Id == withoutBuyerId)).BuyerClientId);
        Assert.Equal(buyerId, (await context.Maintenance.SingleAsync(order => order.Id == withBuyerId)).BuyerClientId);
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_name = 'Maintenance' AND column_name = 'IsBuyerSelectionRecorded'").SingleAsync());

        await migrator.MigrateAsync("20261011145029_AddPaymentCreationOperations");

        Assert.All(await context.Database.SqlQueryRaw<bool>(
            "SELECT \"IsBuyerSelectionRecorded\" AS \"Value\" FROM \"Maintenance\"").ToListAsync(), value => Assert.True(value));
        await migrator.MigrateAsync();
    }
}
