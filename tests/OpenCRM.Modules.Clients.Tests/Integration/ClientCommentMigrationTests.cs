using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Clients.Tests.Integration;

public sealed class ClientCommentMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261019142041_PersistOrganizationServiceHourlyTotals";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();

    public Task InitializeAsync() => this._postgres.StartAsync();

    public async Task DisposeAsync() => await this._postgres.DisposeAsync();

    [Fact]
    public async Task MigratesBothTextsWithoutTruncationAndBlocksDestructiveRollback()
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(this._postgres.GetConnectionString(), database =>
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name))
            .Options;

        await using var context = new OpenCRMContext(options);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var user = new ApplicationUser { UserName ="comment-migration", Email = "migration@example.test" };
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity."AspNetUsers"
                ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "DatetimeCreation",
                 "LastDateOfEntry", "CountVisits", "LastLoginDate", "CountLogins", "IsDeleted",
                 "CanUseTestSubscriptions", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled",
                 "LockoutEnabled", "AccessFailedCount", "PersonalDataProcessingConsentAccepted",
                 "AdvertisingConsentAccepted")
            VALUES ({user.Id}, {user.UserName}, {user.UserName!.ToUpperInvariant()}, {user.Email},
                    {user.Email!.ToUpperInvariant()}, now(), now(), 0, now(), 0, false, false, false, false,
                    false, false, 0, false, false)
            """);
        var organization = new Organization
        {
            Name = "Перенос комментариев",
            OwnerUserId = user.Id,
            PrimaryScenarioCode = "detailing"
        };
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Organizations"
                ("Id", "Name", "OwnerUserId", "PrimaryScenarioCode", "DocumentStyleCode", "MaintenanceWorkflowMode",
                 "AllowWorkSelfAssignment", "SetupAutoOpenPending", "SetupStep", "SetupRevision", "WorkingDays",
                 "IsActive", "IsDeleted", "IsArchived", "CreatedAt", "UpdatedAt")
            VALUES ({organization.Id}, {organization.Name}, {user.Id}, 'detailing', 'auto', 'staged',
                false, false, 0, 0, ARRAY[]::integer[], true, false, false, now(), now())
            """);

        var cases = new (string? Comment, string? Notes, string? Expected)[]
        {
            ("Комментарий", "Заметка", "Комментарий\n\nЗаметка"),
            (null, "Только заметка", "Только заметка"),
            ("Только комментарий", null, "Только комментарий"),
            (null, null, null),
            ("", "Заметка", "Заметка"),
            ("Одинаковый текст", "Одинаковый текст", "Одинаковый текст"),
            ("Первый\nабзац", "Второй\nабзац", "Первый\nабзац\n\nВторой\nабзац"),
            (new string('я', 600), new string('а', 2000), new string('я', 600) + "\n\n" + new string('а', 2000)),
            (" С пробелами ", " Сохранённая заметка ", " С пробелами \n\n Сохранённая заметка ")
        };

        var expected = new Dictionary<Guid, string?>();
        foreach (var item in cases)
        {
            var client = new OrganizationClient
            {
                Organization = organization,
                Name = "Клиент переноса",
                Comment = item.Comment,
                IsDeleted = expected.Count % 2 == 0
            };

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "OrganizationClients" ("Id", "OrganizationId", "Name", "Comment", "IsDeleted", "CreatedAt", "UpdatedAt") VALUES ({client.Id}, {organization.Id}, {client.Name}, {client.Comment}, {client.IsDeleted}, {client.CreatedAt}, {client.UpdatedAt})""");
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"OrganizationClients\" SET \"Notes\" = {item.Notes} WHERE \"Id\" = {client.Id}");
            expected.Add(client.Id, item.Expected);
        }

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();
        var actual = await context.OrganizationClients.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Comment);
        foreach (var item in expected)
        {
            Assert.Equal(item.Value, actual[item.Key]);
        }

        Assert.False(context.Database.HasPendingModelChanges());
        var notesColumnCount = await context.Database.SqlQueryRaw<int>(
            """SELECT COUNT(*)::integer AS "Value" FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'OrganizationClients' AND column_name = 'Notes'""")
            .SingleAsync();
        Assert.Equal(0, notesColumnCount);

        var legacyClients = await context.OrganizationClients.AsNoTracking().ToListAsync();
        Assert.All(legacyClients, item =>
        {
            Assert.Equal("Клиент переноса", item.Name);
            Assert.Null(item.FirstName);
            Assert.Null(item.LastName);
            Assert.Null(item.MiddleName);
            Assert.Null(item.CompanyTypeCode);
        });

        var identityClientId = legacyClients[0].Id;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "OrganizationClients" SET "FirstName" = 'Иван', "LastName" = 'Иванов', "Name" = 'Иванов Иван', "CompanyTypeCode" = 'entrepreneur' WHERE "Id" = {identityClientId}""");
        var identityRollbackError = await Assert.ThrowsAsync<PostgresException>(() =>
            migrator.MigrateAsync("20261020141645_MergeClientNotesIntoComment"));
        Assert.Contains("Откат составных ФИО", identityRollbackError.MessageText);
        var preservedIdentity = await context.OrganizationClients.AsNoTracking().SingleAsync(item => item.Id == identityClientId);
        Assert.Equal("Иванов", preservedIdentity.LastName);
        Assert.Equal("Иван", preservedIdentity.FirstName);
        Assert.Equal("entrepreneur", preservedIdentity.CompanyTypeCode);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "OrganizationClients" SET "FirstName" = NULL, "LastName" = NULL, "CompanyTypeCode" = NULL WHERE "Id" = {identityClientId}""");

        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(PreviousMigration));
        Assert.Contains("Автоматический откат объединённых комментариев невозможен", error.MessageText);
        await migrator.MigrateAsync();
        var afterRollback = await context.OrganizationClients.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Comment);
        Assert.Equal(actual.Count, afterRollback.Count);
        foreach (var item in actual)
        {
            Assert.Equal(item.Value, afterRollback[item.Key]);
        }
    }
}
