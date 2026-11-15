using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Testcontainers.PostgreSql;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class MaintenanceAssignmentMigrationTests : IAsyncLifetime
{
    private const string NormalizeMigration = "20261009011254_NormalizeServiceBundlesAndMaintenanceDetails";
    private const string PreviousMigration = "20261009005033_AddOrganizationWeeklyWorkingHours";
    private const string TargetMigration = "20261009102414_RefreshRelationalMaintenanceAssignments";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        await using var context = this.CreateContext();
        var migrator = context.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        await migrator.MigrateAsync(NormalizeMigration);
    }

    public Task DisposeAsync() => this._postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task MigrationRefreshesAssignmentsAndPreservesDetailsAcrossRollbackAndReapply()
    {
        var ownerId = Guid.NewGuid().ToString();
        var organizationId = Guid.NewGuid();
        var newOrganizationId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var replacementEmployeeId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var initialMaintenanceId = Guid.NewGuid();
        var fallbackMaintenanceId = Guid.NewGuid();
        var transferMaintenanceId = Guid.NewGuid();
        var initialDetailId = Guid.NewGuid();
        var materialDetailId = Guid.NewGuid();
        var fallbackDetailId = Guid.NewGuid();
        var transferDetailId = Guid.NewGuid();
        var historyId = Guid.NewGuid();
        var plannedDate = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);
        var plannedEndDate = plannedDate.AddDays(1);

        await using (var context = this.CreateContext())
        {
            var owner = new ApplicationUser
            {
                Id = ownerId,
                UserName ="migration-test",
                NormalizedUserName ="MIGRATION-TEST",
                DatetimeCreation = DateTime.UtcNow,
                LastDateOfEntry = DateTime.UtcNow,
                LastLoginDate = DateTime.UtcNow
            };
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity."AspNetUsers"
                    ("Id", "UserName", "NormalizedUserName", "DatetimeCreation", "LastDateOfEntry", "CountVisits",
                     "LastLoginDate", "CountLogins", "IsDeleted", "CanUseTestSubscriptions", "EmailConfirmed",
                     "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount",
                     "PersonalDataProcessingConsentAccepted", "AdvertisingConsentAccepted")
                VALUES ({owner.Id}, {owner.UserName}, {owner.NormalizedUserName}, {owner.DatetimeCreation},
                        {owner.LastDateOfEntry}, 0, {owner.LastLoginDate}, 0, false, false, false, false, false,
                        false, 0, false, false)
                """);

            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Organizations" ("Id", "Name", "PrimaryScenarioCode", "OwnerUserId", "IsActive", "CreatedAt", "UpdatedAt",
                    "IsArchived", "IsDeleted", "AllowWorkSelfAssignment", "SetupAutoOpenPending", "SetupRevision", "SetupStep")
                VALUES ({organizationId}, 'Тестовая организация', 'service_station', {ownerId}, true, now(), now(), false, false, false, false, 0, 0)
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Organizations" ("Id", "Name", "PrimaryScenarioCode", "OwnerUserId", "IsActive", "CreatedAt", "UpdatedAt",
                    "IsArchived", "IsDeleted", "AllowWorkSelfAssignment", "SetupAutoOpenPending", "SetupRevision", "SetupStep")
                VALUES ({newOrganizationId}, 'Новая организация', 'service_station', {ownerId}, true, now(), now(), false, false, false, false, 0, 0)
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"OrganizationEmployees\" (\"Id\", \"OrganizationId\", \"Name\", \"IsActive\") VALUES ({employeeId}, {organizationId}, 'Сотрудник', true)");
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Vehicles\" (\"Id\", \"Kind\", \"DateTimeCreation\", \"IsDeleted\", \"IsArchived\", \"UserId\", \"OrganizationId\") VALUES ({vehicleId}, 0, now(), false, false, {ownerId}, {organizationId})");

            await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Maintenance\" DISABLE TRIGGER \"TR_MaintenanceEmployeeAssignments\"");
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Maintenance" ("Id", "Type", "Date", "DateTimeCreation", "UserId", "VehicleId", "IsDeleted",
                    "PlannedDate", "PlannedEndDate", "PlannedStartTime", "PlannedEndTime", "Status", "EmployeeId")
                VALUES ({initialMaintenanceId}, 0, {plannedDate}, now(), {ownerId}, {vehicleId}, false,
                    {plannedDate}, {plannedEndDate}, interval '08:00', interval '09:00', 1, {employeeId})
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Maintenance" ("Id", "Type", "Date", "DateTimeCreation", "UserId", "VehicleId", "IsDeleted",
                    "PlannedDate", "PlannedEndDate", "PlannedStartTime", "PlannedEndTime", "Status", "EmployeeId")
                VALUES ({fallbackMaintenanceId}, 0, {plannedDate}, now(), {ownerId}, {vehicleId}, false,
                    {plannedDate}, {plannedEndDate}, interval '10:00', interval '11:00', 2, {employeeId})
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Maintenance" ("Id", "Type", "Date", "DateTimeCreation", "UserId", "VehicleId", "IsDeleted",
                    "PlannedDate", "PlannedEndDate", "PlannedStartTime", "PlannedEndTime", "Status")
                VALUES ({transferMaintenanceId}, 0, {plannedDate}, now(), {ownerId}, {vehicleId}, false,
                    {plannedDate}, {plannedEndDate}, interval '12:00', interval '13:00', 1)
                """);
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"Maintenance\" ENABLE TRIGGER \"TR_MaintenanceEmployeeAssignments\"");

            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"MaintenanceDetails\" (\"MaintenanceId\", \"DetailId\", \"Position\", \"Name\", \"Price\", \"Quantity\", \"EmployeeId\") VALUES ({initialMaintenanceId}, {initialDetailId}, 0, 'Работа', 1200, 1, {otherEmployeeId})");
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "MaintenanceDetails" ("MaintenanceId", "DetailId", "Position", "Name", "Price", "Quantity", "EmployeeId",
                    "MaterialName", "MaterialQuantity", "MaterialUnit", "MaterialUnitCost", "MaterialTotalCost")
                VALUES ({initialMaintenanceId}, {materialDetailId}, 1, 'Материал', 500, 1, {replacementEmployeeId}, 'Масло', 2, 'л', 100, 200)
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"MaintenanceDetails\" (\"MaintenanceId\", \"DetailId\", \"Position\", \"Name\", \"Price\", \"Quantity\") VALUES ({fallbackMaintenanceId}, {fallbackDetailId}, 0, 'Работа без исполнителя', 800, 1)");
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"MaintenanceDetails\" (\"MaintenanceId\", \"DetailId\", \"Position\", \"Name\", \"Price\", \"Quantity\", \"EmployeeId\") VALUES ({transferMaintenanceId}, {transferDetailId}, 0, 'Работа для переноса', 900, 1, {otherEmployeeId})");
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"MaintenanceHistoryEvents\" (\"Id\", \"MaintenanceId\", \"VehicleId\", \"OrganizationId\", \"UserId\", \"EventType\", \"Title\", \"CreatedAt\") VALUES ({historyId}, {transferMaintenanceId}, {vehicleId}, {organizationId}, {ownerId}, 'test_event', 'История теста', now())");

            var migrator = context.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            await migrator.MigrateAsync(TargetMigration);
        }

        await using (var context = this.CreateContext())
        {
            var initialAssignments = await this.ReadAssignmentsAsync(context, initialMaintenanceId);
            Assert.Collection(initialAssignments,
                assignment =>
                {
                    Assert.Equal(initialDetailId, assignment.DetailId);
                    Assert.Equal(otherEmployeeId, assignment.EmployeeId);
                });
            Assert.DoesNotContain(initialAssignments, assignment => assignment.DetailId == materialDetailId);

            var insertedDetailId = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "MaintenanceDetails" ("MaintenanceId", "DetailId", "Position", "Name", "Price", "Quantity", "EmployeeId")
                VALUES ({initialMaintenanceId}, {insertedDetailId}, 2, 'Дополнительная работа', 300, 1, {replacementEmployeeId});
                """);
            var assignmentsAfterInsert = await this.ReadAssignmentsAsync(context, initialMaintenanceId);
            Assert.Equal(new[] { initialDetailId, insertedDetailId }.Order(), assignmentsAfterInsert.Select(row => row.DetailId).Order().ToArray());

            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MaintenanceDetails\" SET \"EmployeeId\" = {employeeId} WHERE \"MaintenanceId\" = {initialMaintenanceId} AND \"DetailId\" = {insertedDetailId}");
            var assignmentsAfterUpdate = await this.ReadAssignmentsAsync(context, initialMaintenanceId);
            Assert.Contains(assignmentsAfterUpdate, assignment => assignment.DetailId == insertedDetailId && assignment.EmployeeId == employeeId);
            Assert.DoesNotContain(assignmentsAfterUpdate, assignment => assignment.EmployeeId == replacementEmployeeId);

            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"MaintenanceDetails\" WHERE \"MaintenanceId\" = {initialMaintenanceId} AND \"DetailId\" = {initialDetailId}");
            var assignmentsAfterDelete = await this.ReadAssignmentsAsync(context, initialMaintenanceId);
            Assert.Collection(assignmentsAfterDelete,
                assignment =>
                {
                    Assert.Equal(insertedDetailId, assignment.DetailId);
                    Assert.Equal(employeeId, assignment.EmployeeId);
                });

            var fallbackAssignments = await this.ReadAssignmentsAsync(context, fallbackMaintenanceId);
            Assert.Collection(fallbackAssignments,
                assignment =>
                {
                    Assert.Equal(fallbackDetailId, assignment.DetailId);
                    Assert.Equal(employeeId, assignment.EmployeeId);
                });

            var updatedPlannedDate = plannedDate.AddDays(3);
            var updatedPlannedEndDate = plannedEndDate.AddDays(3);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Maintenance" SET "PlannedDate" = {updatedPlannedDate}, "PlannedEndDate" = {updatedPlannedEndDate}, "Status" = 3
                WHERE "Id" = {fallbackMaintenanceId};
                """);
            var updatedFallback = Assert.Single(await this.ReadAssignmentsAsync(context, fallbackMaintenanceId));
            Assert.Equal(updatedPlannedDate, updatedFallback.PlannedDate);
            Assert.Equal(updatedPlannedEndDate, updatedFallback.PlannedEndDate);
            Assert.Equal(3, updatedFallback.Status);

            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Vehicles\" SET \"OrganizationId\" = {newOrganizationId} WHERE \"Id\" = {vehicleId}");
            var transferredAssignment = Assert.Single(await this.ReadAssignmentsAsync(context, transferMaintenanceId));
            Assert.Equal(newOrganizationId, transferredAssignment.OrganizationId);

            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Maintenance\" WHERE \"Id\" = {initialMaintenanceId}");
            Assert.Empty(await this.ReadAssignmentsAsync(context, initialMaintenanceId));

            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Maintenance\" SET \"EmployeeId\" = NULL WHERE \"Id\" = {fallbackMaintenanceId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"OrganizationEmployees\" WHERE \"Id\" = {employeeId}");

            var migrator = context.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            await migrator.MigrateAsync(NormalizeMigration);
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Maintenance\" SET \"Status\" = 4 WHERE \"Id\" = {fallbackMaintenanceId}");
            Assert.Equal(4, await context.Database.SqlQuery<int>($"SELECT \"Status\" AS \"Value\" FROM \"Maintenance\" WHERE \"Id\" = {fallbackMaintenanceId}").SingleAsync());

            var beforeRollback = await this.ReadDetailsAsync(context, transferMaintenanceId);
            await migrator.MigrateAsync(PreviousMigration);
            var jsonAfterRollback = await context.Database.SqlQuery<string>($"SELECT \"Details\" AS \"Value\" FROM \"Maintenance\" WHERE \"Id\" = {transferMaintenanceId}").SingleAsync();
            Assert.Contains(transferDetailId.ToString(), jsonAfterRollback, StringComparison.OrdinalIgnoreCase);
            await migrator.MigrateAsync(TargetMigration);
            var afterReapply = await this.ReadDetailsAsync(context, transferMaintenanceId);
            Assert.Equal(beforeRollback, afterReapply);
            Assert.Equal(1, await context.Database.SqlQuery<int>($"SELECT count(*) AS \"Value\" FROM \"MaintenanceHistoryEvents\" WHERE \"Id\" = {historyId}").SingleAsync());
        }
    }

    private OpenCRMContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(this._postgres.GetConnectionString(), db =>
                db.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name))
            .Options;

        return new OpenCRMContext(options);
    }

    private async Task<List<AssignmentRecord>> ReadAssignmentsAsync(OpenCRMContext context, Guid maintenanceId)
    {
        return await this.ReadRowsAsync(context, """
            SELECT "OrganizationId", "DetailId", "EmployeeId", "PlannedDate", "PlannedEndDate", "Status"
            FROM "MaintenanceEmployeeAssignments" WHERE "MaintenanceId" = @maintenanceId ORDER BY "DetailId"
            """, maintenanceId, reader => new AssignmentRecord(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4), reader.GetInt32(5)));
    }

    private async Task<List<DetailRecord>> ReadDetailsAsync(OpenCRMContext context, Guid maintenanceId)
    {
        return await this.ReadRowsAsync(context, """
            SELECT "DetailId", "Position", "Price", "Name" FROM "MaintenanceDetails"
            WHERE "MaintenanceId" = @maintenanceId ORDER BY "Position"
            """, maintenanceId, reader => new DetailRecord(
                reader.GetGuid(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetDouble(2), reader.GetString(3)));
    }

    private async Task<List<T>> ReadRowsAsync<T>(OpenCRMContext context, string sql, Guid maintenanceId, Func<DbDataReader, T> map)
    {
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "maintenanceId";
        parameter.Value = maintenanceId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<T>();
        while (await reader.ReadAsync())
        {
            result.Add(map(reader));
        }

        await context.Database.CloseConnectionAsync();

        return result;
    }

    private sealed record AssignmentRecord(Guid OrganizationId, Guid DetailId, Guid EmployeeId, DateTime? PlannedDate, DateTime? PlannedEndDate, int Status);

    private sealed record DetailRecord(Guid DetailId, int Position, double? Price, string Name);
}
