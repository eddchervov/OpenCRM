using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

public sealed class OrganizationDataExportMigrationTests
{
    private const string PreviousMigration = "20261015071909_RemoveAccountProfiles";
    private const string ExportMigration = "20261015092404_AddOrganizationDataExportRequestDate";

    [Fact]
    public async Task MigrationPreservesLatestRequestAndRestoresAuditOnRollback()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(postgres.GetConnectionString(), database =>
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name))
            .Options;
        await using var context = new OpenCRMContext(options);
        await context.Database.MigrateAsync();

        var organization = CreateOrganization(new OpenCRM.Domain.Identity.ApplicationUser
        {
            Email = $"owner-{Guid.NewGuid():N}@example.test"
        });
        var emptyOrganization = CreateOrganization(new OpenCRM.Domain.Identity.ApplicationUser
        {
            Email = $"owner-{Guid.NewGuid():N}@example.test"
        });
        context.Organizations.AddRange(organization, emptyOrganization);
        await context.SaveChangesAsync();
        var requestedAt = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
        context.OrganizationAuditEvents.AddRange(
            CreateAudit(organization, requestedAt.AddDays(-1)),
            CreateAudit(organization, requestedAt),
            new OrganizationAuditEvent
            {
                OrganizationId = organization.Id,
                ActorUserId = organization.OwnerUserId,
                EventType = "organization.updated",
                Title = "Изменена организация",
                CreatedAt = requestedAt.AddHours(2)
            });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var migrator = context.GetService<IMigrator>();

        try
        {
            await migrator.MigrateAsync(PreviousMigration);
            await migrator.MigrateAsync(ExportMigration);

            Assert.Equal(requestedAt, await context.Organizations.Where(value => value.Id == organization.Id)
                .Select(value => value.LastDataExportRequestedAt).SingleAsync());
            Assert.Null(await context.Organizations.Where(value => value.Id == emptyOrganization.Id)
                .Select(value => value.LastDataExportRequestedAt).SingleAsync());
            Assert.False(context.Database.HasPendingModelChanges());

            var nextRequestedAt = requestedAt.AddHours(1);
            await context.Organizations.Where(value => value.Id == organization.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.LastDataExportRequestedAt, nextRequestedAt));
            await context.OrganizationAuditEvents.Where(value => value.OrganizationId == organization.Id
                && value.EventType == "organization.data-export.requested").ExecuteDeleteAsync();

            await migrator.MigrateAsync(PreviousMigration);

            var restored = await context.OrganizationAuditEvents.Where(value => value.OrganizationId == organization.Id
                    && value.EventType == "organization.data-export.requested")
                .Select(value => value.CreatedAt).SingleAsync();

            Assert.Equal(nextRequestedAt, restored);

            await migrator.MigrateAsync(ExportMigration);

            Assert.Equal(nextRequestedAt, await context.Organizations.Where(value => value.Id == organization.Id)
                .Select(value => value.LastDataExportRequestedAt).SingleAsync());
        }
        finally
        {
            await migrator.MigrateAsync();
        }
    }

    private static Organization CreateOrganization(OpenCRM.Domain.Identity.ApplicationUser owner) => new()
    {
        Name = "Организация для проверки выгрузки",
        PrimaryScenarioCode = "detailing",
        OwnerUser = owner,
        Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
    };

    private static OrganizationAuditEvent CreateAudit(Organization organization, DateTime requestedAt) => new()
    {
        OrganizationId = organization.Id,
        ActorUserId = organization.OwnerUserId,
        EventType = "organization.data-export.requested",
        Title = "Запрошена выгрузка данных организации",
        CreatedAt = requestedAt
    };
}
