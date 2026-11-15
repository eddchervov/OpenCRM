using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности подписки пользователя. </summary>
public sealed class UserSubscriptionEntityTypeConfiguration : IEntityTypeConfiguration<UserSubscription>
{
    /// <summary> Настраивает отображение сущности подписки пользователя. </summary>
    public void Configure(EntityTypeBuilder<UserSubscription> builder)
    {
        builder.HasOne(subscription => subscription.SubscriptionPurchase).WithMany()
            .HasForeignKey(subscription => subscription.SubscriptionPurchaseId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(subscription => subscription.SourceCode)
            .HasMaxLength(16)
            .HasDefaultValue("unknown");
    }
}
