using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности уведомления на сайте. </summary>
public sealed class SiteNotificationEntityTypeConfiguration : IEntityTypeConfiguration<SiteNotification>
{
    /// <summary> Настраивает отображение сущности уведомления на сайте. </summary>
    public void Configure(EntityTypeBuilder<SiteNotification> builder)
    {
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Title).HasMaxLength(200).IsRequired();
        builder.Property(notification => notification.Body).HasMaxLength(4000).IsRequired();
        builder.Property(notification => notification.TypeCode).HasMaxLength(64).IsRequired();
        builder.Property(notification => notification.EventKey).HasMaxLength(200);
        builder.HasIndex(notification => notification.CreatedAt);
        builder.HasIndex(notification => notification.EventKey).IsUnique();
        builder.HasIndex(notification => notification.RecipientUserId);
        builder.HasOne<OpenCRM.Domain.Identity.ApplicationUser>()
            .WithMany()
            .HasForeignKey(notification => notification.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
