using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности закрытия уведомления на сайте. </summary>
public sealed class SiteNotificationClosureEntityTypeConfiguration : IEntityTypeConfiguration<SiteNotificationClosure>
{
    /// <summary> Настраивает отображение сущности закрытия уведомления на сайте. </summary>
    public void Configure(EntityTypeBuilder<SiteNotificationClosure> builder)
    {
        builder.HasKey(closure => new { closure.NotificationId, closure.UserId });
        builder.HasOne(closure => closure.Notification)
            .WithMany(notification => notification.Closures)
            .HasForeignKey(closure => closure.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(closure => closure.User)
            .WithMany()
            .HasForeignKey(closure => closure.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
