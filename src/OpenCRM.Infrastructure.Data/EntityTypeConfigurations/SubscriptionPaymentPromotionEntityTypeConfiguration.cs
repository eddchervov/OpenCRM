using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности платежа за подписку. </summary>
public class SubscriptionPaymentPromotionEntityTypeConfiguration : IEntityTypeConfiguration<SubscriptionPayment>
{
    /// <summary> Настраивает отображение сущности платежа за подписку. </summary>
    public void Configure(EntityTypeBuilder<SubscriptionPayment> builder)
    {
        builder.Property(payment => payment.PaymentProviderCode).HasMaxLength(32).HasDefaultValue("yookassa");
        builder.Property(payment => payment.OriginalAmount).HasPrecision(18, 2);
        builder.Property(payment => payment.PromoDiscountAmount).HasPrecision(18, 2);
        builder.Property(payment => payment.RefundedAmount).HasPrecision(18, 2);
        builder.Property(payment => payment.PromoCodeSnapshot).HasMaxLength(64);
        builder.Property(payment => payment.BuyerTypeCode).HasMaxLength(32).HasDefaultValue("unknown");
        builder.Property(payment => payment.BuyerName).HasMaxLength(200);
        builder.Property(payment => payment.BuyerInn).HasMaxLength(12);
        builder.Property(payment => payment.NpdReceiptMarkedByUserId).HasMaxLength(450);
        builder.Property(payment => payment.NpdReceiptReason).HasMaxLength(500);
        builder.Property(payment => payment.NpdReceiptUrl).HasMaxLength(2048);
        builder.Property(payment => payment.NpdReceiptComment).HasMaxLength(1000);
        builder.Property(payment => payment.NpdReceiptFilePath).HasMaxLength(300);
        builder.Property(payment => payment.NpdReceiptFileName).HasMaxLength(200);
        builder.Property(payment => payment.NpdReceiptFileContentType).HasMaxLength(64);
        builder.Property(payment => payment.NpdReceiptDeliverySourceHash).HasMaxLength(64);
        builder.HasIndex(payment => payment.LastReconciledAt);
        builder.HasIndex(payment => payment.PromoCodeId);
        builder.HasIndex(payment => payment.ExternalPaymentId)
            .IsUnique()
            .HasFilter("\"ExternalPaymentId\" IS NOT NULL");
        builder.HasOne(payment => payment.PromoCode)
            .WithMany()
            .HasForeignKey(payment => payment.PromoCodeId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<PromoCodeApplication>()
            .WithMany()
            .HasForeignKey(payment => payment.PromoCodeApplicationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
