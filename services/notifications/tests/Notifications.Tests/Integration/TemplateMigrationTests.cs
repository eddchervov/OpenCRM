using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Notifications.Domain;
using Notifications.Infrastructure;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Notifications.Tests.Integration;

/// <summary>Проверяет обновление заполненной старой базы и ограничение обратной миграции.</summary>
[Trait("Category", "Integration")]
public sealed class TemplateMigrationTests
{
    /// <summary> Проверяет сохранение старых данных при обновлении и запрет нескольких версий при откате. </summary>
    [Fact]
    public async Task UpgradePreservesOldDataAndDowngradeRejectsMultipleVersions()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(postgres.GetConnectionString(),
                provider => provider.MigrationsHistoryTable("__EFMigrationsHistory", "notifications"))
            .Options;
        await using var db = new NotificationDbContext(options);
        var migrator = db.GetService<IMigrator>();
        const string initialMigration = "20260828152723_InitialCreate";
        await migrator.MigrateAsync(initialMigration);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO notifications."NotificationTemplates"
                ("ApplicationId", "Key", "Channel", "SubjectTemplate", "BodyTemplate", "IsHtml", "IsActive", "UpdatedAt")
            VALUES ('opencrm', 'legacy', 1, 'Тема', '<p>Привет</p>', true, true, now());
            INSERT INTO notifications."Notifications"
                ("Id", "ApplicationId", "IdempotencyKey", "Channel", "Recipient", "TemplateKey",
                 "DataJson", "Subject", "Body", "Status", "AttemptCount", "CreatedAt")
            VALUES ('cc62e258-17d4-4e98-bb0b-f89d1f286185', 'opencrm', 'legacy-job', 1,
                    'contact@example.invalid', 'legacy', jsonb_build_object(), NULL, NULL, 1, 0, now());
            """);
        await migrator.MigrateAsync();

        var template = await db.NotificationTemplates.SingleAsync();
        Assert.Equal(1, template.Version);
        Assert.Equal("<p>Привет</p>", template.BodyTemplate);
        Assert.True(template.IsActive);
        Assert.False(db.Database.HasPendingModelChanges());
        var notification = await db.Notifications.SingleAsync();
        Assert.Equal("legacy-job", notification.IdempotencyKey);
        Assert.Equal("legacy", notification.TemplateKey);
        Assert.Null(notification.IsHtml);
        Assert.Null(notification.TemplateVersion);
        Assert.Null(notification.Body);

        // Пока дополнительных версий нет, откат сохраняет исходный шаблон.
        await migrator.MigrateAsync(initialMigration);
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(1, (await db.NotificationTemplates.SingleAsync()).Version);

        db.NotificationTemplates.Add(new NotificationTemplate(
            "opencrm", "legacy", template.Channel, "Новая", "<p>Версия 2</p>", true, DateTimeOffset.UtcNow, 2));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(initialMigration));
        Assert.Equal(2, await db.NotificationTemplates.CountAsync());
    }
}
