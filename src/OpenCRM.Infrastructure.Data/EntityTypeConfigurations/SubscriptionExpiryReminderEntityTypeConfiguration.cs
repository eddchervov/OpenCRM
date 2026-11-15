using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности напоминания об истечении подписки. </summary>
public class SubscriptionExpiryReminderEntityTypeConfiguration : IEntityTypeConfiguration<SubscriptionExpiryReminder>
{
    /// <summary> Настраивает отображение сущности напоминания об истечении подписки. </summary>
    public void Configure(EntityTypeBuilder<SubscriptionExpiryReminder> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ScopeCode)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(x => x.RecipientEmail)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(x => x.EventCode)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        builder.HasIndex(x => new
        {
            x.ScopeCode,
            x.SubscriptionId,
            x.SubscriptionEndDate,
            x.EventCode
        }).IsUnique();

        builder.HasIndex(x => x.SentAt);
        builder.HasIndex(x => x.CreatedAt);
    }
}
