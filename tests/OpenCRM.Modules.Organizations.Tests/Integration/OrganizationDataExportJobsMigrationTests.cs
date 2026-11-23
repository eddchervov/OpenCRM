using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

public sealed class OrganizationDataExportJobsMigrationTests
{
    [Fact]
    public async Task QueueMigrationRollsBackWithoutChangingOrganizationAndAudit()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(postgres.GetConnectionString(), database =>
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name)).Options;
        await using var context = new OpenCRMContext(options);
        var migrator = context.GetService<IMigrator>();
        const string supportAttachmentsMigration = "20261029070300_AddSupportAttachments";
        const string exportJobsMigration = "20261030065903_AddOrganizationDataExportJobs";
        await migrator.MigrateAsync();
        var owner = new OpenCRM.Domain.Identity.ApplicationUser();
        var organization = new Organization
        {
            Name = "QA миграция экспорта", OwnerUser = owner,
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var audit = new OrganizationAuditEvent
        {
            OrganizationId = organization.Id, ActorUserId = owner.Id,
            EventType = "organization.updated", Title = "QA история"
        };
        context.OrganizationAuditEvents.Add(audit);
        await context.SaveChangesAsync();

        await migrator.MigrateAsync(supportAttachmentsMigration);
        await migrator.MigrateAsync(exportJobsMigration);
        var exportJobId = Guid.NewGuid();
        var queuedState = "queued";
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "OrganizationDataExportJobs" ("Id", "OrganizationId", "RequestedByUserId", "State", "RequestedAtUtc")
            VALUES ({exportJobId}, {organization.Id}, {owner.Id}, {queuedState}, {DateTime.UtcNow})
            """);
        Assert.False(context.Database.HasPendingModelChanges());
        context.ChangeTracker.Clear();
        await migrator.MigrateAsync(supportAttachmentsMigration);
        await migrator.MigrateAsync();

        Assert.Equal("QA миграция экспорта", await context.Organizations.Where(value => value.Id == organization.Id)
            .Select(value => value.Name).SingleAsync());
        Assert.True(await context.OrganizationAuditEvents.AnyAsync(value => value.Id == audit.Id));

        Assert.Empty(await context.Set<OrganizationDataExportJob>().ToArrayAsync());
        Assert.False(context.Database.HasPendingModelChanges());
    }
    [Fact]
    public async Task AuditDescriptionMigrationPreservesHistoryAndRejectsLossyRollback()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(postgres.GetConnectionString(), database =>
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name)).Options;
        await using var context = new OpenCRMContext(options);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync();
        var owner = new OpenCRM.Domain.Identity.ApplicationUser();
        var organization = new Organization { Name = "QA длинный аудит", OwnerUser = owner };
        var audit = new OrganizationAuditEvent
        {
            Organization = organization, ActorUserId = owner.Id, EventType = "permissions.role.updated",
            Title = "QA права роли", Description = new string('Я', 1400)
        };
        context.Add(audit);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            migrator.MigrateAsync("20261030065903_AddOrganizationDataExportJobs"));

        Assert.Equal("P0001", error.SqlState);
        Assert.Equal(1400, await context.OrganizationAuditEvents.Where(value => value.Id == audit.Id)
            .Select(value => value.Description!.Length).SingleAsync());
        await context.OrganizationAuditEvents.Where(value => value.Id == audit.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(value => value.Description, "QA короткая история"));
        await migrator.MigrateAsync("20261030065903_AddOrganizationDataExportJobs");
        Assert.Equal("QA короткая история", await context.OrganizationAuditEvents.Where(value => value.Id == audit.Id)
            .Select(value => value.Description).SingleAsync());
        await migrator.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
