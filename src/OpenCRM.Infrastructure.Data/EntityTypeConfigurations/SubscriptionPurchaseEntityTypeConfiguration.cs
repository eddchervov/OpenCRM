using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Реляционные ограничения покупки подписки и резерва получателя доступа. </summary>
public sealed class SubscriptionPurchaseEntityTypeConfiguration : IEntityTypeConfiguration<SubscriptionPurchase>
{
    public void Configure(EntityTypeBuilder<SubscriptionPurchase> builder)
    {
        builder.ToTable("SubscriptionPurchases", table =>
        {
            table.HasCheckConstraint("CK_SubscriptionPurchases_Amounts", "\"AmountMinor\" > 0 AND \"RefundedAmountMinor\" >= 0 AND \"RefundedAmountMinor\" <= \"AmountMinor\"");
            table.HasCheckConstraint("CK_SubscriptionPurchases_Version", "\"ProviderVersion\" >= 0");
        });
        builder.HasKey(purchase => purchase.Id);
        builder.Property(purchase => purchase.UserId).HasMaxLength(450);
        builder.Property(purchase => purchase.BeneficiaryKey).HasMaxLength(500);
        builder.Property(purchase => purchase.Currency).HasMaxLength(3);
        builder.Property(purchase => purchase.PlanSnapshot).HasColumnType("jsonb");
        builder.Property(purchase => purchase.ProviderStatus).HasMaxLength(32);
        builder.Property(purchase => purchase.CreationStatus).HasMaxLength(32);
        builder.Property(purchase => purchase.ApplicationStatus).HasMaxLength(32);
        builder.Property(purchase => purchase.LastErrorCode).HasMaxLength(128);
        builder.HasIndex(purchase => purchase.BeneficiaryKey).IsUnique().HasFilter("\"IsOpen\"");
        builder.HasIndex(purchase => new { purchase.ApplicationStatus, purchase.NextApplicationAttemptAt });
        builder.HasIndex(purchase => purchase.NextReconcileAt);
        builder.HasIndex(purchase => purchase.ProviderOrderId).IsUnique().HasFilter("\"ProviderOrderId\" IS NOT NULL");
        builder.HasIndex(purchase => purchase.CreationOperationId).IsUnique();
        builder.HasIndex(purchase => purchase.SubscriptionPaymentId).IsUnique();
        builder.HasOne(purchase => purchase.CreationOperation).WithOne().HasForeignKey<SubscriptionPurchase>(purchase => purchase.CreationOperationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(purchase => purchase.Payment).WithOne().HasForeignKey<SubscriptionPurchase>(purchase => purchase.SubscriptionPaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(purchase => purchase.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(purchase => purchase.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
