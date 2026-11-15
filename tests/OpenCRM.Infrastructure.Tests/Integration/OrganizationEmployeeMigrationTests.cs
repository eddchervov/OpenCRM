using OpenCRM.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using Npgsql;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class OrganizationEmployeeMigrationTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Fact]
    public async Task MigrationClearsOnlyOrganizationManualNamesAndRestoresThemOnEmptyDirectoryRollback()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Миграция сотрудников", OwnerUser = user };
        var vehicle = new Vehicle { User = user, Organization = organization, RegistrationPlate = "А123АА154" };
        var personal = new Vehicle { User = user, RegistrationPlate = "В123ВВ154" };
        var work = new Maintenance
        {
            User = user, Vehicle = vehicle, Performer = "Ручное имя", Status = MaintenanceStatus.Planned,
            DateTimeCreation = DateTime.UtcNow, Date = DateTime.UtcNow, Details = []
        };
        var personalWork = new Maintenance
        {
            User = user, Vehicle = personal, Performer = "Личный сервис", Status = MaintenanceStatus.Planned,
            DateTimeCreation = DateTime.UtcNow, Date = DateTime.UtcNow, Details = []
        };
        context.Maintenance.AddRange(work, personalWork);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260916150624_PreserveFullMaintenanceHistory");
        await migrator.MigrateAsync("20260916153126_AddOrganizationEmployees");

        Assert.Null(await context.Database.SqlQuery<string?>($"""SELECT "Performer" AS "Value" FROM "Maintenance" WHERE "Id" = {work.Id}""").SingleAsync());
        Assert.Equal("Личный сервис", await context.Database.SqlQuery<string>($"""SELECT "Performer" AS "Value" FROM "Maintenance" WHERE "Id" = {personalWork.Id}""").SingleAsync());
        var history = Assert.Single(await context.MaintenanceHistoryEvents.Where(item => item.MaintenanceId == work.Id).ToListAsync());
        Assert.Equal("Системное обновление", history.ActorDisplayName);
        Assert.Contains("Ручное имя", history.Description);
        Assert.Contains("Ручное имя", history.PayloadJson);

        await migrator.MigrateAsync("20260916150624_PreserveFullMaintenanceHistory");
        var restored = await context.Database.SqlQuery<string>($"""SELECT "Performer" AS "Value" FROM "Maintenance" WHERE "Id" = {work.Id}""").SingleAsync();
        Assert.Equal("Ручное имя", restored);
        await migrator.MigrateAsync("20260916153126_AddOrganizationEmployees");
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "OrganizationEmployees" ("Id", "OrganizationId", "Name", "IsActive")
            VALUES ({Guid.NewGuid()}, {organization.Id}, {"Мастер"}, true)
            """);
        var rollbackError = await Assert.ThrowsAsync<PostgresException>(() =>
            migrator.MigrateAsync("20260916150624_PreserveFullMaintenanceHistory"));

        Assert.Equal("P0001", rollbackError.SqlState);
        Assert.Contains("справочник сотрудников уже заполнен", rollbackError.MessageText);
        Assert.True(await context.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM "OrganizationEmployees" WHERE "OrganizationId" = {organization.Id}) AS "Value"
            """).SingleAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
