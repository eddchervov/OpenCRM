using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Сохраняет команды администратора отдельно от подтверждённых денежных событий. </summary>
public sealed class SubscriptionPaymentAdminActionEntityTypeConfiguration : IEntityTypeConfiguration<SubscriptionPaymentAdminAction>
{
    public void Configure(EntityTypeBuilder<SubscriptionPaymentAdminAction> builder)
    {
        builder.ToTable("SubscriptionPaymentAdminActions", table =>
            table.HasCheckConstraint("CK_SubscriptionPaymentAdminActions_Amount", "\"AmountMinor\" IS NULL OR \"AmountMinor\" > 0"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Action).HasMaxLength(32);
        builder.Property(item => item.ActorUserId).HasMaxLength(450);
        builder.Property(item => item.Reason).HasMaxLength(500);
        builder.Property(item => item.RequestHash).HasMaxLength(64);
        builder.Property(item => item.StateToken).HasMaxLength(64);
        builder.Property(item => item.VerifiedStateToken).HasMaxLength(64);
        builder.Property(item => item.Status).HasMaxLength(32);
        builder.Property(item => item.Code).HasMaxLength(128);
        builder.HasIndex(item => new { item.PurchaseId, item.CreatedAt });
        builder.HasIndex(item => new { item.CompletedAt, item.NextAttemptAt });
        builder.HasOne(item => item.Purchase).WithMany().HasForeignKey(item => item.PurchaseId).OnDelete(DeleteBehavior.Restrict);
    }
}
