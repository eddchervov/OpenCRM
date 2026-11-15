using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности исходящих Telegram-уведомлений. </summary>
public sealed class TelegramNotificationOutboxEntityTypeConfiguration : IEntityTypeConfiguration<TelegramNotificationOutbox>
{
    /// <summary> Настраивает отображение сущности исходящих Telegram-уведомлений. </summary>
    public void Configure(EntityTypeBuilder<TelegramNotificationOutbox> builder)
    {
        builder.HasIndex(item => item.NotificationKey).IsUnique();
        builder.HasIndex(item => item.NotificationCommandId);
        builder.HasIndex(item => item.NextDispatchAt);
        builder.Property(item => item.NotificationKey).HasMaxLength(200);
        builder.Property(item => item.Body).HasMaxLength(4000);
        builder.Property(item => item.NotificationStatus).HasMaxLength(40);
        builder.Property(item => item.ProviderMessageId).HasMaxLength(200);
        builder.Property(item => item.ErrorCode).HasMaxLength(200);
    }
}
