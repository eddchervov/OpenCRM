using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности промокода. </summary>
public class PromoCodeEntityTypeConfiguration : IEntityTypeConfiguration<PromoCode>
{
    /// <summary> Настраивает отображение сущности промокода. </summary>
    public void Configure(EntityTypeBuilder<PromoCode> builder)
    {
        builder.HasKey(promoCode => promoCode.Id);
        builder.Property(promoCode => promoCode.Code)
            .HasColumnType("citext")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(promoCode => promoCode.Name).HasMaxLength(160).IsRequired();
        builder.Property(promoCode => promoCode.Description).HasMaxLength(1000);
        builder.Property(promoCode => promoCode.DiscountTypeCode).HasMaxLength(32).IsRequired();
        builder.Property(promoCode => promoCode.DiscountValue).HasPrecision(18, 2);
        // Значение по умолчанию, чтобы существующие коды работали после миграции.
        builder.Property(promoCode => promoCode.AppliesToAllScenarios).HasDefaultValue(true);
        builder.HasIndex(promoCode => promoCode.Code).IsUnique();
        builder.HasIndex(promoCode => new { promoCode.IsActive, promoCode.ValidFrom, promoCode.ValidUntil });
        builder.HasOne(promoCode => promoCode.GiftTemplatePlan)
            .WithMany()
            .HasForeignKey(promoCode => promoCode.GiftTemplatePlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
