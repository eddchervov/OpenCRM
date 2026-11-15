using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности применения промокода. </summary>
public class PromoCodeApplicationEntityTypeConfiguration : IEntityTypeConfiguration<PromoCodeApplication>
{
    /// <summary> Настраивает отображение сущности применения промокода. </summary>
    public void Configure(EntityTypeBuilder<PromoCodeApplication> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_PromoCodeApplications_Status",
                "\"Status\" IN ('assigned', 'claimed', 'consumed')");
            table.HasCheckConstraint(
                "CK_PromoCodeApplications_Timestamps",
                "(\"Status\" = 'consumed' AND \"ConsumedAt\" IS NOT NULL) OR " +
                "(\"Status\" <> 'consumed' AND \"ConsumedAt\" IS NULL)");
        });
        builder.HasKey(application => application.Id);
        builder.Property(application => application.Status).HasMaxLength(32).IsRequired();
        builder.Property(application => application.ApplyActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(application => application.BenefitTypeCode).HasMaxLength(32).IsRequired();
        builder.Property(application => application.DiscountValue).HasPrecision(18, 2);
        // Код и организация уникальны бессрочно, независимо от статуса применения.
        builder.HasIndex(application => new { application.PromoCodeId, application.OrganizationId })
            .IsUnique();
        builder.HasIndex(application => application.PaymentCreationOperationId)
            .IsUnique()
            .HasFilter("\"PaymentCreationOperationId\" IS NOT NULL");
        builder.HasIndex(application => application.SubscriptionPaymentId)
            .IsUnique()
            .HasFilter("\"SubscriptionPaymentId\" IS NOT NULL");
        builder.HasIndex(application => new { application.PromoCodeId, application.Status });
        builder.HasOne(application => application.PromoCode)
            .WithMany(promoCode => promoCode.Applications)
            .HasForeignKey(application => application.PromoCodeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(application => application.Organization)
            .WithMany()
            .HasForeignKey(application => application.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(application => application.PaymentCreationOperation)
            .WithMany()
            .HasForeignKey(application => application.PaymentCreationOperationId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(application => application.SubscriptionPayment)
            .WithMany()
            .HasForeignKey(application => application.SubscriptionPaymentId)
            .OnDelete(DeleteBehavior.SetNull);
        // Несколько применений могут ссылаться на одну выданную подписку.
        builder.HasOne(application => application.GrantedSubscription)
            .WithMany()
            .HasForeignKey(application => application.GrantedSubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
