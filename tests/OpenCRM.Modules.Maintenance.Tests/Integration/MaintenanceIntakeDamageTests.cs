using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using OpenCRM.Infrastructure.Migrations;
using Npgsql;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceIntakeDamageTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task DamageSurvivesReopeningAndUpdatesWithoutChangingWorkflowOrLegacyIntake()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeDamageTests.CreateWork();
        work.IntakeVisibleDamage = "Старые замечания";
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceIntakeDamageTests.CreateScope(context);
        var service = scope.Get<IMaintenanceIntakeDamageService>();
        var organizationId = work.Vehicle.Organization!.Id;
        var id = Guid.NewGuid();
        await service.SaveDamage(work.User.Id, organizationId, work.Id, id, MaintenanceIntakeDamageTests.Request(work.Version));
        var firstTime = work.IntakeRecordedAt;
        var firstVersion = work.Version;
        var update = MaintenanceIntakeDamageTests.Request(firstVersion);
        update.Description = "Уточнённая царапина";
        update.X = 0.75;
        await service.SaveDamage(work.User.Id, organizationId, work.Id, id, update);

        await using var reopened = fixture.CreateContext();
        using var reopenedScope = MaintenanceIntakeDamageTests.CreateScope(reopened);
        var result = await reopenedScope.Get<IMaintenanceIntakeDamageService>().GetDamages(work.User.Id, organizationId, work.Id);
        var damage = Assert.Single(result.Damages);
        Assert.Equal(MaintenanceIntakeDiagramVersions.SedanV1, result.DiagramVersion);
        Assert.Equal(id, damage.Id);
        Assert.Equal(1, damage.Number);
        Assert.Equal("Уточнённая царапина", damage.Description);
        Assert.Equal(0.75, damage.X);
        var saved = await reopened.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(MaintenanceIntakeDiagramVersions.SedanV1, saved.IntakeDiagramVersion);
        Assert.Equal(MaintenanceStatus.Planned, saved.Status);
        Assert.Equal("Старые замечания", saved.IntakeVisibleDamage);
        Assert.NotNull(firstTime);
        Assert.Equal(firstTime, saved.IntakeRecordedAt);
        Assert.NotEqual(firstVersion, result.Version);
        var history = await reopened.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Contains(history, value => value.Description!.Contains("Уточнённая царапина"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DeletionKeepsIdentitiesAndPhotosWithContiguousVisibleNumbers(int deletedIndex)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeDamageTests.CreateWork();
        var photo = MaintenanceIntakeDamageTests.Photo(work, MaintenancePhotoStage.Before);
        context.MaintenancePhotos.Add(photo);
        await context.SaveChangesAsync();
        using var scope = MaintenanceIntakeDamageTests.CreateScope(context);
        var service = scope.Get<IMaintenanceIntakeDamageService>();
        var organizationId = work.Vehicle.Organization!.Id;
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in ids)
        {
            var request = MaintenanceIntakeDamageTests.Request(work.Version);
            request.PhotoIds = [photo.Id];
            await service.SaveDamage(work.User.Id, organizationId, work.Id, id, request);
        }

        var historyBeforeDeletion = await context.MaintenanceHistoryEvents.AsNoTracking()
            .Where(value => value.MaintenanceId == work.Id).ToDictionaryAsync(value => value.Id, value => value.Description);
        await service.DeleteDamage(work.User.Id, organizationId, work.Id, ids[deletedIndex], work.Version);
        var addedId = Guid.NewGuid();
        await service.SaveDamage(work.User.Id, organizationId, work.Id, addedId, MaintenanceIntakeDamageTests.Request(work.Version));

        var result = await service.GetDamages(work.User.Id, organizationId, work.Id);
        var remainingIds = ids.Where(id => id != ids[deletedIndex]).Append(addedId).ToArray();
        Assert.Equal([1, 2, 3], result.Damages.Select(damage => damage.Number));
        Assert.Equal(remainingIds, result.Damages.Select(damage => damage.Id));
        Assert.All(result.Damages.Where(damage => damage.Id != addedId), damage => Assert.Equal([photo.Id], damage.PhotoIds));
        var persisted = await context.MaintenanceIntakeDamages.AsNoTracking()
            .Where(damage => damage.MaintenanceId == work.Id).OrderBy(damage => damage.Number).ToArrayAsync();
        Assert.Equal([1, 2, 3, 4], persisted.Select(damage => damage.Number));
        Assert.True(persisted[deletedIndex].IsDeleted);
        var historyAfterDeletion = await context.MaintenanceHistoryEvents.AsNoTracking()
            .Where(value => value.MaintenanceId == work.Id).ToListAsync();
        Assert.All(historyBeforeDeletion, entry => Assert.Equal(entry.Value,
            historyAfterDeletion.Single(value => value.Id == entry.Key).Description));
        var deletion = Assert.Single(historyAfterDeletion, value => value.EventType == "intake_damage_deleted");
        using var payload = JsonDocument.Parse(deletion.PayloadJson!);
        Assert.Equal(ids[deletedIndex], payload.RootElement.GetProperty("damageId").GetGuid());
        Assert.Equal(MaintenanceIntakeDiagramVersions.SedanV1, payload.RootElement.GetProperty("diagramVersion").GetString());

        var editedId = remainingIds[0];
        var update = MaintenanceIntakeDamageTests.Request(result.Version);
        update.PhotoIds = [photo.Id];
        update.Description = "Уточнение после перенумерации";
        await service.SaveDamage(work.User.Id, organizationId, work.Id, editedId, update);
        var updated = Assert.Single((await service.GetDamages(work.User.Id, organizationId, work.Id)).Damages, damage => damage.Id == editedId);
        Assert.Equal(1, updated.Number);
        Assert.Equal("Уточнение после перенумерации", updated.Description);
        Assert.Equal([photo.Id], updated.PhotoIds);

        foreach (var id in remainingIds)
        {
            await service.DeleteDamage(work.User.Id, organizationId, work.Id, id, work.Version);
        }

        Assert.Empty((await service.GetDamages(work.User.Id, organizationId, work.Id)).Damages);
        await service.SaveDamage(work.User.Id, organizationId, work.Id, Guid.NewGuid(), MaintenanceIntakeDamageTests.Request(work.Version));
        Assert.Equal(1, Assert.Single((await service.GetDamages(work.User.Id, organizationId, work.Id)).Damages).Number);
        Assert.Equal(5, await context.MaintenanceIntakeDamages.Where(damage => damage.MaintenanceId == work.Id).MaxAsync(damage => damage.Number));
    }

    [Fact]
    public async Task StaleVersionCannotOverwriteOrDeleteAnotherUsersChange()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeDamageTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceIntakeDamageTests.CreateScope(context);
        var service = scope.Get<IMaintenanceIntakeDamageService>();
        var organizationId = work.Vehicle.Organization!.Id;
        var staleVersion = work.Version;
        var id = Guid.NewGuid();
        await service.SaveDamage(work.User.Id, organizationId, work.Id, id, MaintenanceIntakeDamageTests.Request(staleVersion));

        var conflict = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.SaveDamage(
            work.User.Id, organizationId, work.Id, id, MaintenanceIntakeDamageTests.Request(staleVersion)));
        Assert.Equal(staleVersion, conflict.Data["ExpectedVersion"]);
        Assert.Equal(work.Version, conflict.Data["ActualVersion"]);
        Assert.DoesNotContain("другим пользователем", conflict.Message);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.DeleteDamage(
            work.User.Id, organizationId, work.Id, id, staleVersion));
        Assert.Single((await service.GetDamages(work.User.Id, organizationId, work.Id)).Damages);
    }

    [Fact]
    public async Task PhotosMustBelongToTheSameOrderAndBeActiveBeforePhotos()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeDamageTests.CreateWork();
        var other = MaintenanceIntakeDamageTests.CreateWork();
        var valid = MaintenanceIntakeDamageTests.Photo(work, MaintenancePhotoStage.Before);
        var foreign = MaintenanceIntakeDamageTests.Photo(other, MaintenancePhotoStage.Before);
        var after = MaintenanceIntakeDamageTests.Photo(work, MaintenancePhotoStage.After);
        var deleted = MaintenanceIntakeDamageTests.Photo(work, MaintenancePhotoStage.Before);
        deleted.IsDeleted = true;
        context.MaintenancePhotos.AddRange(valid, foreign, after, deleted);
        await context.SaveChangesAsync();
        using var scope = MaintenanceIntakeDamageTests.CreateScope(context);
        var service = scope.Get<IMaintenanceIntakeDamageService>();
        var organizationId = work.Vehicle.Organization!.Id;
        foreach (var photoId in new[] { foreign.Id, after.Id, deleted.Id, Guid.NewGuid() })
        {
            var invalid = MaintenanceIntakeDamageTests.Request(work.Version);
            invalid.PhotoIds = [photoId];
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDamage(
                work.User.Id, organizationId, work.Id, Guid.NewGuid(), invalid));
        }

        var request = MaintenanceIntakeDamageTests.Request(work.Version);
        request.PhotoIds = [valid.Id];
        var damageId = Guid.NewGuid();
        await service.SaveDamage(work.User.Id, organizationId, work.Id, damageId, request);
        Assert.Equal([valid.Id], Assert.Single((await service.GetDamages(work.User.Id, organizationId, work.Id)).Damages).PhotoIds);
        valid.IsDeleted = true;
        await context.SaveChangesAsync();
        Assert.Empty(Assert.Single((await service.GetDamages(work.User.Id, organizationId, work.Id)).Damages).PhotoIds);
    }

    [Theory]
    [InlineData("unknown", "scratch", 0.5, 0.5)]
    [InlineData("front", "unknown", 0.5, 0.5)]
    [InlineData("front", "scratch", -0.1, 0.5)]
    [InlineData("front", "scratch", 0.5, 1.1)]
    [InlineData("front", "scratch", double.NaN, 0.5)]
    public async Task InvalidMarkersAreRejectedWithoutHistory(string view, string type, double x, double y)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeDamageTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceIntakeDamageTests.CreateScope(context);
        var request = MaintenanceIntakeDamageTests.Request(work.Version);
        request.ViewCode = view;
        request.DamageType = type;
        request.X = x;
        request.Y = y;
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Get<IMaintenanceIntakeDamageService>().SaveDamage(
            work.User.Id, work.Vehicle.Organization!.Id, work.Id, Guid.NewGuid(), request));

        Assert.False(await context.MaintenanceIntakeDamages.AnyAsync(damage => damage.MaintenanceId == work.Id));
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(value => value.MaintenanceId == work.Id));
    }

    [Fact]
    public async Task AccessChecksPreventForeignOrganizationReadsAndReadOnlyWrites()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeDamageTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var access = new Mock<IOrganizationSubscriptionService>();
        access.Setup(value => value.EnsureWorkspaceCanWriteAsync("viewer", work.Vehicle.Organization!.Id, OrganizationPermissionCodes.MaintenanceEdit))
            .ThrowsAsync(new InvalidOperationException("Только для чтения"));
        using var scope = MaintenanceIntakeDamageTests.CreateScope(context, access.Object);
        var service = scope.Get<IMaintenanceIntakeDamageService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDamage(
            "viewer", work.Vehicle.Organization!.Id, work.Id, Guid.NewGuid(), MaintenanceIntakeDamageTests.Request(work.Version)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetDamages(work.User.Id, Guid.NewGuid(), work.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDamage(
            work.User.Id, Guid.NewGuid(), work.Id, Guid.NewGuid(), MaintenanceIntakeDamageTests.Request(work.Version)));
        access.VerifyAll();
    }

    [Fact]
    public async Task FirstSavedDiagramSurvivesReopeningAndRejectsUnknownGeometry()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeDamageTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceIntakeDamageTests.CreateScope(context);
        var service = scope.Get<IMaintenanceIntakeDamageService>();
        var organizationId = work.Vehicle.Organization!.Id;
        var request = MaintenanceIntakeDamageTests.Request(work.Version);
        var id = Guid.NewGuid();
        await service.SaveDamage(work.User.Id, organizationId, work.Id, id, request);
        var saved = await service.GetDamages(work.User.Id, organizationId, work.Id);
        Assert.Equal(MaintenanceIntakeDiagramVersions.SedanV1, saved.DiagramVersion);
        request.Version = saved.Version;
        request.DiagramVersion = "future-diagram-v9";
        request.X = 0.9;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDamage(work.User.Id, organizationId, work.Id, id, request));

        await using var reopened = fixture.CreateContext();
        using var reopenedScope = MaintenanceIntakeDamageTests.CreateScope(reopened);
        var result = await reopenedScope.Get<IMaintenanceIntakeDamageService>().GetDamages(work.User.Id, organizationId, work.Id);
        Assert.Equal(MaintenanceIntakeDiagramVersions.SedanV1, result.DiagramVersion);
        Assert.Equal(0.5, Assert.Single(result.Damages).X);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown-version")]
    public async Task MissingOrUnknownDiagramCannotSaveCoordinates(string diagramVersion)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeDamageTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceIntakeDamageTests.CreateScope(context);
        var request = MaintenanceIntakeDamageTests.Request(work.Version);
        request.DiagramVersion = diagramVersion;

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Get<IMaintenanceIntakeDamageService>().SaveDamage(
            work.User.Id, work.Vehicle.Organization!.Id, work.Id, Guid.NewGuid(), request));

        Assert.Null(work.IntakeDiagramVersion);
        Assert.False(await context.MaintenanceIntakeDamages.AnyAsync(value => value.MaintenanceId == work.Id));
    }

    [Fact]
    public async Task ReadingAnUnpinnedOrderDoesNotPersistTheSuggestedDiagram()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeDamageTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceIntakeDamageTests.CreateScope(context);
        var result = await scope.Get<IMaintenanceIntakeDamageService>().GetDamages(work.User.Id, work.Vehicle.Organization!.Id, work.Id);

        Assert.Equal(MaintenanceIntakeDiagramVersions.Current, result.DiagramVersion);
        Assert.Null(work.IntakeDiagramVersion);
    }

    [Fact]
    public async Task PersistedDiagramIsReturnedVerbatimAndCannotBeOverwrittenByCurrentCoordinates()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeDamageTests.CreateWork();
        work.IntakeDiagramVersion = "archived-unavailable";
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceIntakeDamageTests.CreateScope(context);
        var service = scope.Get<IMaintenanceIntakeDamageService>();
        var organizationId = work.Vehicle.Organization!.Id;
        Assert.Equal("archived-unavailable", (await service.GetDamages(work.User.Id, organizationId, work.Id)).DiagramVersion);
        var damageId = Guid.NewGuid();
        context.MaintenanceIntakeDamages.Add(new MaintenanceIntakeDamage
        {
            Id = damageId, MaintenanceId = work.Id, Number = 1,
            ViewCode = "rear", DamageType = "dent", X = 0.62, Y = 0.45
        });
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteDamage(work.User.Id, organizationId, work.Id,
            damageId, work.Version));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.SaveDamage(work.User.Id, organizationId, work.Id,
            Guid.NewGuid(), MaintenanceIntakeDamageTests.Request(work.Version)));

        Assert.Equal("archived-unavailable", work.IntakeDiagramVersion);
        var savedDamage = Assert.Single(await context.MaintenanceIntakeDamages.Where(value => value.MaintenanceId == work.Id).ToListAsync());
        Assert.False(savedDamage.IsDeleted);
    }

    [Fact]
    public async Task PinnedDiagramCannotBeChangedOrClearedInTheDatabase()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeDamageTests.CreateWork();
        work.IntakeDiagramVersion = MaintenanceIntakeDiagramVersions.SedanV1;
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Maintenance" SET "IntakeDiagramVersion" = 'replacement' WHERE "Id" = {work.Id}"""));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Maintenance" SET "IntakeDiagramVersion" = NULL WHERE "Id" = {work.Id}"""));

        await context.Entry(work).ReloadAsync();
        Assert.Equal(MaintenanceIntakeDiagramVersions.SedanV1, work.IntakeDiagramVersion);
    }

    [Fact]
    public async Task MigrationBackfillsLegacyIntakeAndGuardsPinnedDiagramAndRollback()
    {
        await using var admin = fixture.CreateContext();
        var databaseName = "intake_version_" + Guid.NewGuid().ToString("N");
        var connection = new NpgsqlConnectionStringBuilder(admin.Database.GetConnectionString()) { Database = databaseName };
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(connection.ConnectionString, database => database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name))
            .Options;
        await using var isolated = new OpenCRMContext(options);
        await isolated.GetService<IRelationalDatabaseCreator>().CreateAsync();
        const string communicationsMigration = "20261023152920_AddUniqueMaxBotIdentity";
        const string intakeMigration = "20261024101148_AddMaintenanceIntakeDamages";
        await isolated.Database.MigrateAsync();

        var textIntake = MaintenanceIntakeDamageTests.CreateWork();
        textIntake.IntakeRecordedAt = DateTime.UtcNow;
        var unstarted = MaintenanceIntakeDamageTests.CreateWork();
        textIntake.IntakeVisibleDamage = "Старая отметка повреждения";
        var legacyOptions = new DbContextOptionsBuilder<OpenCRMContext>(options)
            .ReplaceService<IModelCustomizer, LegacyIntakeSeedModelCustomizer>().Options;
        await using (var legacySeed = new OpenCRMContext(legacyOptions))
        {
            legacySeed.Vehicles.AddRange(textIntake.Vehicle, unstarted.Vehicle);
            await legacySeed.SaveChangesAsync();
        }

        await isolated.GetService<IMigrator>().MigrateAsync(communicationsMigration);

        foreach (var work in new[] { textIntake, unstarted })
        {
            await isolated.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Maintenance"
                    ("Id", "Type", "Date", "Location", "DateTimeCreation", "UserId", "VehicleId", "IsDeleted",
                     "IntakeRecordedAt", "IntakeVisibleDamage")
                VALUES
                    ({work.Id}, {(int)work.Type}, {work.Date}, {work.Location}, {work.DateTimeCreation}, {work.User.Id},
                     {work.Vehicle.Id}, false, {work.IntakeRecordedAt}, {work.IntakeVisibleDamage})
                """);
        }

        var migrator = isolated.GetService<IMigrator>();
        await migrator.MigrateAsync();

        var saved = await isolated.Maintenance.AsNoTracking().ToDictionaryAsync(value => value.Id);
        Assert.Equal(MaintenanceIntakeDiagramVersions.SedanV1, saved[textIntake.Id].IntakeDiagramVersion);
        Assert.Null(saved[unstarted.Id].IntakeDiagramVersion);
        Assert.Equal("Старая отметка повреждения", saved[textIntake.Id].IntakeVisibleDamage);

        await Assert.ThrowsAsync<PostgresException>(() => isolated.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Maintenance" SET "IntakeDiagramVersion" = NULL WHERE "Id" = {textIntake.Id}"""));
        await isolated.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Maintenance" SET "IntakeDiagramVersion" = 'sedan-v1' WHERE "Id" = {unstarted.Id}""");
        await Assert.ThrowsAsync<PostgresException>(() => isolated.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Maintenance" SET "IntakeDiagramVersion" = NULL WHERE "Id" = {unstarted.Id}"""));

        await isolated.Database.ExecuteSqlRawAsync("ALTER TABLE \"Maintenance\" DISABLE TRIGGER \"TR_Maintenance_IntakeDiagramVersion\"");
        await isolated.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "Maintenance" SET "IntakeDiagramVersion" = 'future-v2' WHERE "Id" = {textIntake.Id}""");
        await isolated.Database.ExecuteSqlRawAsync("ALTER TABLE \"Maintenance\" ENABLE TRIGGER \"TR_Maintenance_IntakeDiagramVersion\"");
        await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(communicationsMigration));
        Assert.Contains(intakeMigration, await isolated.Database.GetAppliedMigrationsAsync());
        Assert.Equal("future-v2", await isolated.Maintenance.Where(value => value.Id == textIntake.Id)
            .Select(value => value.IntakeDiagramVersion).SingleAsync());
    }

    private static SaveMaintenanceIntakeDamageDto Request(uint version) => new()
    {
        Version = version, DiagramVersion = MaintenanceIntakeDiagramVersions.SedanV1, ViewCode = "left", DamageType = "scratch", X = 0.5, Y = 0.5, Description = "Царапина на двери"
    };

    private sealed class LegacyIntakeSeedModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<Organization>().Ignore(value => value.VisitReminderHoursBefore);
            modelBuilder.Entity<Organization>().Ignore(value => value.VisitReminderChannelCode);
            modelBuilder.Entity<Organization>().Ignore(value => value.VisitReminderTemplate);
        }
    }

    private static MaintenancePhoto Photo(MaintenanceEntity work, MaintenancePhotoStage stage) => new()
    {
        Id = Guid.NewGuid(), Maintenance = work, Vehicle = work.Vehicle, User = work.User,
        Path = "test/intake.jpg", OriginalFileName = "intake.jpg", ContentType = "image/jpeg", Stage = stage
    };

    private static MaintenanceTestScope CreateScope(OpenCRMContext context, IOrganizationSubscriptionService? access = null)
    {
        if (access == null)
        {
            var subscriptions = new Mock<IOrganizationSubscriptionService>();
            subscriptions.Setup(service => service.HasFeatureAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), Helpers.FeatureCodes.MaintenanceIntake))
                .ReturnsAsync(true);
            access = subscriptions.Object;
        }

        return new MaintenanceTestScope(new MaintenanceRepository(context), null!, Mock.Of<ISystemClock>(clock => clock.UtcNow == new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc)), context,
            access, Mock.Of<IMaintenanceQueryService>(), Mock.Of<IMaintenanceSlotQueryService>(), null!);
    }

    private static MaintenanceEntity CreateWork()
    {
        var user = new ApplicationUser();
        var vehicle = new Vehicle
        {
            User = user, RegistrationPlate = "А123АА154",
            Organization = new Organization
            {
                Name = "Осмотр детейлинга", OwnerUser = user,
                Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
            }
        };

        return new MaintenanceEntity
        {
            Vehicle = vehicle, User = user, Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance, Date = DateTime.UtcNow, DateTimeCreation = DateTime.UtcNow,
            Details = [new() { Name = "Полировка" }]
        };
    }
}
