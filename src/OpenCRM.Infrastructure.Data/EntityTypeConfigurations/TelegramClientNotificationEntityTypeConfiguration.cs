using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности уведомления клиента в Telegram. </summary>
public sealed class TelegramClientNotificationEntityTypeConfiguration : IEntityTypeConfiguration<TelegramClientNotification>
{
    /// <summary> Настраивает отображение сущности уведомления клиента в Telegram. </summary>
    public void Configure(EntityTypeBuilder<TelegramClientNotification> builder)
    {
        builder.HasIndex(x => x.NotificationKey).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt });
        builder.HasIndex(x => new { x.MaintenanceId, x.VisitStartsAtUtc })
            .HasFilter("\"EventCode\" = 'booking_reminder'");
        builder.Property(x => x.EventCode).HasMaxLength(32);
        builder.Property(x => x.NotificationKey).HasMaxLength(160);
        builder.Property(x => x.Body).HasMaxLength(4000);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.ProviderMessageId).HasMaxLength(200);
        builder.Property(x => x.ErrorCode).HasMaxLength(100);
    }
}
