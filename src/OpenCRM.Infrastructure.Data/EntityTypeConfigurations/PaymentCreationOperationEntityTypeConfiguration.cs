using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности операции создания платежа. </summary>
public sealed class PaymentCreationOperationEntityTypeConfiguration : IEntityTypeConfiguration<PaymentCreationOperation>
{
    /// <summary> Настраивает отображение сущности операции создания платежа. </summary>
    public void Configure(EntityTypeBuilder<PaymentCreationOperation> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_PaymentCreationOperations_Status",
                "\"Status\" IN ('prepared', 'sending', 'created', 'rejected', 'manual_review')");
            table.HasCheckConstraint(
                "CK_PaymentCreationOperations_Lease",
                "(\"Status\" = 'sending' AND \"LeaseToken\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL) OR (\"Status\" <> 'sending' AND \"LeaseToken\" IS NULL AND \"LeaseUntil\" IS NULL)");
        });
        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.PaymentProviderCode).HasMaxLength(32).HasDefaultValue("yookassa");
        builder.Property(operation => operation.IdempotenceKey).HasMaxLength(64).IsRequired();
        builder.HasIndex(operation => operation.IdempotenceKey).IsUnique();
        builder.Property(operation => operation.UserId).HasMaxLength(450).IsRequired();
        builder.Property(operation => operation.OperationType).HasMaxLength(256).IsRequired();
        builder.Property(operation => operation.Amount).HasPrecision(18, 2);
        builder.Property(operation => operation.OriginalAmount).HasPrecision(18, 2);
        builder.Property(operation => operation.PromoDiscountAmount).HasPrecision(18, 2);
        builder.Property(operation => operation.PromoCodeSnapshot).HasMaxLength(64);
        builder.Property(operation => operation.Description).HasMaxLength(256).IsRequired();
        builder.Property(operation => operation.ReturnUrl).HasMaxLength(2048);
        builder.Property(operation => operation.BuyerTypeCode).HasMaxLength(32).IsRequired();
        builder.Property(operation => operation.BuyerName).HasMaxLength(200);
        builder.Property(operation => operation.BuyerInn).HasMaxLength(12);
        builder.Property(operation => operation.RequestBody).HasColumnType("text").IsRequired();
        builder.Property(operation => operation.Status).HasMaxLength(32).IsRequired();
        builder.Property(operation => operation.LastAttemptAt);
        builder.Property(operation => operation.ExternalPaymentId).HasMaxLength(128);
        builder.Property(operation => operation.PaymentLink).HasMaxLength(2048);
        builder.Property(operation => operation.LastErrorCode).HasMaxLength(128);
        builder.HasIndex(operation => new { operation.Status, operation.LeaseUntil });
        builder.HasIndex(operation => operation.ExternalPaymentId)
            .IsUnique()
            .HasFilter("\"ExternalPaymentId\" IS NOT NULL");
        builder.HasIndex(operation => operation.SubscriptionPaymentId).IsUnique();
        builder.HasOne(operation => operation.SubscriptionPlan)
            .WithMany()
            .HasForeignKey(operation => operation.SubscriptionPlanId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(operation => operation.User)
            .WithMany()
            .HasForeignKey(operation => operation.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(operation => operation.PromoCode)
            .WithMany()
            .HasForeignKey(operation => operation.PromoCodeId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(operation => operation.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PromoCodeApplication>()
            .WithMany()
            .HasForeignKey(operation => operation.PromoCodeApplicationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(operation => operation.SubscriptionPayment)
            .WithOne()
            .HasForeignKey<PaymentCreationOperation>(operation => operation.SubscriptionPaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
