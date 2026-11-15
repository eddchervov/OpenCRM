using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Notifications.Application;
using Notifications.Domain;

namespace Notifications.Infrastructure;

/// <summary>
/// Представляет хранилище уведомлений, шаблонов, попыток и transactional outbox.
/// </summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options), IUnitOfWork
{
    /// <summary>Набор уведомлений и их текущих состояний.</summary>
    public DbSet<Notification> Notifications => this.Set<Notification>();

    /// <summary>Неизменяемый журнал попыток доставки.</summary>
    public DbSet<NotificationAttempt> NotificationAttempts => this.Set<NotificationAttempt>();

    /// <summary>Набор шаблонов, изолированных по приложениям.</summary>
    public DbSet<NotificationTemplate> NotificationTemplates => this.Set<NotificationTemplate>();

    /// <summary>Входящие события внешних провайдеров.</summary>
    public DbSet<ProviderWebhookEvent> ProviderWebhookEvents => this.Set<ProviderWebhookEvent>();

    /// <summary>Идемпотентное состояние внешней доставки сообщений Telegram.</summary>
    public DbSet<TelegramChatDeliveryState> TelegramChatDeliveryStates => this.Set<TelegramChatDeliveryState>();

    /// <summary>Надёжно принятые команды доставки переписок.</summary>
    public DbSet<ConversationDeliveryInbox> ConversationDeliveryInbox => this.Set<ConversationDeliveryInbox>();

    /// <summary>Настраивает схему, ограничения, индексы и таблицы MassTransit outbox.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notifications");

        modelBuilder.Entity<Notification>(builder =>
        {
            builder.ToTable("Notifications");
            builder.HasKey(item => item.Id);
            builder.Property(item => item.ApplicationId).HasMaxLength(100);
            builder.Property(item => item.IdempotencyKey).HasMaxLength(200);
            builder.Property(item => item.Recipient).HasMaxLength(500);
            builder.Property(item => item.TemplateKey).HasMaxLength(200);
            builder.Property(item => item.DataJson).HasColumnType("jsonb");
            builder.Property(item => item.Subject).HasMaxLength(1000);
            builder.Property(item => item.LastError).HasMaxLength(2000);
            // Уникальный индекс обеспечивает идемпотентность даже при параллельных API-запросах.
            builder.HasIndex(item => new { item.ApplicationId, item.IdempotencyKey }).IsUnique();
            builder.HasIndex(item => new { item.Status, item.ScheduledAt, item.DispatchRequestedAt });
            builder.HasMany(item => item.Attempts)
                .WithOne()
                .HasForeignKey(item => item.NotificationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationAttempt>(builder =>
        {
            builder.ToTable("NotificationAttempts");
            builder.HasKey(item => item.Id);
            builder.Property(item => item.ProviderMessageId).HasMaxLength(500);
            builder.Property(item => item.Error).HasMaxLength(2000);
            builder.HasIndex(item => new { item.NotificationId, item.Number });
        });

        modelBuilder.Entity<NotificationTemplate>(builder =>
        {
            builder.ToTable("NotificationTemplates");
            builder.HasKey(item => new { item.ApplicationId, item.Key, item.Channel, item.Version });
            builder.Property(item => item.ApplicationId).HasMaxLength(100);
            builder.Property(item => item.Key).HasMaxLength(200);
            builder.Property(item => item.SubjectTemplate).HasMaxLength(1000);
        });

        modelBuilder.Entity<ProviderWebhookEvent>(builder =>
        {
            builder.HasKey(item => item.Id);
            builder.Property(item => item.ApplicationId).HasMaxLength(100);
            builder.Property(item => item.Payload).HasColumnType("jsonb");
            builder.HasIndex(item => item.ReceivedAt);
        });

        modelBuilder.Entity<TelegramChatDeliveryState>(builder =>
        {
            builder.ToTable("TelegramChatDeliveryStates");
            builder.HasKey(item => item.LeaseId);
            builder.Property(item => item.Status).HasMaxLength(32);
            builder.Property(item => item.ProviderMessageId).HasMaxLength(100);
            builder.Property(item => item.ErrorCode).HasMaxLength(64);
            builder.Property(item => item.ErrorMessage).HasMaxLength(512);
            builder.Property(item => item.RetryAfterAt);
            builder.Property(item => item.StartedAt);
            builder.HasIndex(item => item.RequestId);
            builder.HasIndex(item => item.MessageId);
            builder.HasIndex(item => item.UpdatedAt);
        });

        modelBuilder.Entity<ConversationDeliveryInbox>(builder =>
        {
            builder.ToTable("ConversationDeliveryInbox");
            builder.HasKey(item => new { item.ProviderCode, item.MessageId, item.RequestId });
            builder.Property(item => item.ProviderCode).HasMaxLength(16);
            builder.Property(item => item.Status).HasMaxLength(24);
            builder.Property(item => item.LastErrorCode).HasMaxLength(64);
            builder.HasIndex(item => new { item.ProviderCode, item.Status, item.NextAttemptAt, item.LeaseExpiresAt });
            builder.HasIndex(item => item.CreatedAt);
        });

        // Inbox и outbox обеспечивают согласованность данных с доставкой событий RabbitMQ.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }

    /// <summary> Сохраняет изменения в хранилище уведомлений. </summary>
    Task<int> IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        return this.SaveChangesAsync(cancellationToken);
    }
}
