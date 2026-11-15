using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class RelationalOrderMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261009005033_AddOrganizationWeeklyWorkingHours";
    private const string TargetMigration = "20261009011254_NormalizeServiceBundlesAndMaintenanceDetails";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        await using var context = this.CreateContext();
        await RelationalOrderMigrationTests.GetMigrator(context).MigrateAsync(PreviousMigration);
    }

    public Task DisposeAsync() => this._postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task MigrationPreservesOrderDetailsAndBundleMembershipAcrossRollbackAndReapply()
    {
        var data = await this.SeedAsync();
        await using var context = this.CreateContext();
        var migrator = RelationalOrderMigrationTests.GetMigrator(context);

        await migrator.MigrateAsync(TargetMigration);

        Assert.True(await this.DetailsMatchAsync(context, data));
        Assert.True(await this.BundleItemsMatchAsync(context, data));

        await migrator.MigrateAsync(PreviousMigration);

        Assert.True(await this.LegacyDetailsMatchAsync(context, data));
        Assert.True(await this.LegacyBundleIdsMatchAsync(context, data));

        await migrator.MigrateAsync(TargetMigration);

        Assert.True(await this.DetailsMatchAsync(context, data));
        Assert.True(await this.BundleItemsMatchAsync(context, data));
    }

    [Fact]
    public async Task MigrationRejectsDuplicateBundleLinksAndOrderLineIdsWithoutChangingLegacyData()
    {
        var data = await this.SeedAsync(duplicateBundleService: true, duplicateOrderLine: true);
        await using var context = this.CreateContext();
        var migrator = RelationalOrderMigrationTests.GetMigrator(context);

        var duplicateBundleError = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(TargetMigration));
        Assert.Contains("повторяющаяся услуга", duplicateBundleError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await this.LegacyDetailsMatchAsync(context, data));
        Assert.True(await this.LegacyBundleIdsMatchAsync(context, data));
        Assert.True(await context.Database.SqlQuery<bool>($"SELECT to_regclass('\"MaintenanceDetails\"') IS NULL AS \"Value\"").SingleAsync());

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "OrganizationScenarioCatalogItems" SET "ServiceIds" = ARRAY[{data.ServiceId}]
            WHERE "Id" = {data.BundleId}
            """);
        data = data with { ExpectedBundleServiceIds = [data.ServiceId] };

        var duplicateDetailError = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(TargetMigration));
        Assert.Contains("повторяется DetailId", duplicateDetailError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await this.LegacyDetailsMatchAsync(context, data));
        Assert.True(await this.LegacyBundleIdsMatchAsync(context, data));
    }

    private OpenCRMContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(this._postgres.GetConnectionString(), db =>
                db.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name))
            .Options;

        return new OpenCRMContext(options);
    }

    private async Task<SeedData> SeedAsync(bool duplicateBundleService = false, bool duplicateOrderLine = false)
    {
        var ownerId = Guid.NewGuid().ToString();
        var organizationId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var maintenanceId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var secondServiceId = Guid.NewGuid();
        var firstDetailId = Guid.NewGuid();
        var secondDetailId = duplicateOrderLine ? firstDetailId : Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var catalogItemId = Guid.NewGuid();
        var materialCatalogItemId = Guid.NewGuid();
        var detailsJson = $$"""
            [
              {
                "DetailId": "{{firstDetailId}}", "Name": "Полировка кузова", "Description": "Историческое описание",
                "Price": 1250.5, "VatRateCode": "vat_20", "EmployeeId": "{{employeeId}}", "EmployeeName": "Иван",
                "CatalogItemId": "{{catalogItemId}}", "Quantity": 2, "MaterialCatalogItemId": null,
                "MaterialName": null, "MaterialQuantity": null, "MaterialUnit": null,
                "MaterialUnitCost": null, "MaterialTotalCost": null
              },
              {
                "DetailId": "{{secondDetailId}}", "Name": "Замена фильтра", "Description": null,
                "Price": 800, "VatRateCode": null, "EmployeeId": null, "EmployeeName": null,
                "CatalogItemId": null, "Quantity": 1, "MaterialCatalogItemId": "{{materialCatalogItemId}}",
                "MaterialName": "Масляный фильтр", "MaterialQuantity": 1.5, "MaterialUnit": "шт.",
                "MaterialUnitCost": 320.25, "MaterialTotalCost": 480.375
              }
            ]
            """;
        var serviceIds = duplicateBundleService
            ? new[] { serviceId, serviceId }
            : new[] { serviceId, secondServiceId };

        await using var context = this.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity."AspNetUsers"
                ("Id", "UserName", "NormalizedUserName", "DatetimeCreation", "LastDateOfEntry", "CountVisits",
                 "LastLoginDate", "CountLogins", "IsDeleted", "CanUseTestSubscriptions", "EmailConfirmed",
                 "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount",
                 "PersonalDataProcessingConsentAccepted", "AdvertisingConsentAccepted")
            VALUES ({ownerId}, {"relational-migration-test"}, {"RELATIONAL-MIGRATION-TEST"}, now(), now(), 0,
                    now(), 0, false, false, false, false, false, false, 0, false, false)
            """);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Organizations" ("Id", "Name", "PrimaryScenarioCode", "OwnerUserId", "IsActive", "CreatedAt", "UpdatedAt",
                "IsArchived", "IsDeleted", "AllowWorkSelfAssignment", "SetupAutoOpenPending", "SetupRevision", "SetupStep")
            VALUES ({organizationId}, 'Миграционная проверка', 'service_station', {ownerId}, true, now(), now(), false, false, false, false, 0, 0)
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Vehicles" ("Id", "Kind", "DateTimeCreation", "IsDeleted", "IsArchived", "UserId", "OrganizationId")
            VALUES ({vehicleId}, 0, now(), false, false, {ownerId}, {organizationId})
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "OrganizationScenarioCatalogItems" ("Id", "OrganizationId", "ScenarioCode", "KindCode", "Code", "Name", "SortOrder", "IsSystem", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES
                ({bundleId}, {organizationId}, 'service_station', 'service_bundle', 'bundle', 'Комплекс', 0, false, true, now(), now()),
                ({serviceId}, {organizationId}, 'service_station', 'service', 'service-one', 'Первая услуга', 1, false, true, now(), now()),
                ({secondServiceId}, {organizationId}, 'service_station', 'service', 'service-two', 'Вторая услуга', 2, false, true, now(), now())
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "OrganizationScenarioCatalogItems" SET "ServiceIds" = {serviceIds}
            WHERE "Id" = {bundleId}
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Maintenance" ("Id", "Type", "Date", "DateTimeCreation", "UserId", "VehicleId", "IsDeleted", "Details")
            VALUES ({maintenanceId}, 0, now(), now(), {ownerId}, {vehicleId}, false, {detailsJson})
            """);

        var expectedBundleServiceIds = duplicateBundleService ? new[] { serviceId, serviceId } : new[] { serviceId, secondServiceId };

        return new SeedData(
            maintenanceId,
            bundleId,
            serviceId,
            detailsJson,
            expectedBundleServiceIds);
    }

    private async Task<bool> DetailsMatchAsync(OpenCRMContext context, SeedData data)
    {
        return await context.Database.SqlQuery<bool>($"""
            SELECT {data.DetailsJson}::jsonb = COALESCE((
                SELECT jsonb_agg(jsonb_build_object(
                    'DetailId', line."DetailId", 'Name', line."Name", 'Description', line."Description",
                    'Price', line."Price", 'VatRateCode', line."VatRateCode", 'EmployeeId', line."EmployeeId",
                    'EmployeeName', line."EmployeeName", 'CatalogItemId', line."CatalogItemId", 'Quantity', line."Quantity",
                    'MaterialCatalogItemId', line."MaterialCatalogItemId", 'MaterialName', line."MaterialName",
                    'MaterialQuantity', line."MaterialQuantity", 'MaterialUnit', line."MaterialUnit",
                    'MaterialUnitCost', line."MaterialUnitCost", 'MaterialTotalCost', line."MaterialTotalCost"
                ) ORDER BY line."Position") FROM "MaintenanceDetails" line WHERE line."MaintenanceId" = {data.MaintenanceId}
            ), '[]'::jsonb) AS "Value"
            """).SingleAsync();
    }

    private async Task<bool> BundleItemsMatchAsync(OpenCRMContext context, SeedData data)
    {
        var expectedIds = data.ExpectedBundleServiceIds.Select(id => id.ToString()).ToArray();
        var expected = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(expectedIds));
        var actual = JsonNode.Parse(await RelationalOrderMigrationTests.ReadJsonAsync(context, $"""
            SELECT COALESCE(jsonb_agg("ServiceId" ORDER BY "Position"), '[]'::jsonb)::text AS "Value"
            FROM "OrganizationServiceBundleItems" WHERE "BundleId" = {data.BundleId}
            """));

        return JsonNode.DeepEquals(expected, actual);
    }

    private async Task<bool> LegacyDetailsMatchAsync(OpenCRMContext context, SeedData data)
    {
        return await context.Database.SqlQuery<bool>($"""
            SELECT "Details"::jsonb = {data.DetailsJson}::jsonb AS "Value"
            FROM "Maintenance" WHERE "Id" = {data.MaintenanceId}
            """).SingleAsync();
    }

    private async Task<bool> LegacyBundleIdsMatchAsync(OpenCRMContext context, SeedData data)
    {
        var expected = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(data.ExpectedBundleServiceIds.Select(id => id.ToString())));
        var actual = JsonNode.Parse(await RelationalOrderMigrationTests.ReadJsonAsync(context, $"""
            SELECT to_jsonb("ServiceIds")::text AS "Value"
            FROM "OrganizationScenarioCatalogItems" WHERE "Id" = {data.BundleId}
            """));

        return JsonNode.DeepEquals(expected, actual);
    }

    private static async Task<string> ReadJsonAsync(OpenCRMContext context, FormattableString query)
    {
        return await context.Database.SqlQuery<string>(query).SingleAsync();
    }

    private static Microsoft.EntityFrameworkCore.Migrations.IMigrator GetMigrator(OpenCRMContext context)
    {
        return context.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
    }

    private sealed record SeedData(
        Guid MaintenanceId,
        Guid BundleId,
        Guid ServiceId,
        string DetailsJson,
        Guid[] ExpectedBundleServiceIds);
}
