using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности погашения промокода. </summary>
public class PromoCodeRedemptionEntityTypeConfiguration : IEntityTypeConfiguration<PromoCodeRedemption>
{
    /// <summary> Настраивает отображение сущности погашения промокода. </summary>
    public void Configure(EntityTypeBuilder<PromoCodeRedemption> builder)
    {
        builder.HasKey(redemption => redemption.Id);
        builder.HasIndex(redemption => new { redemption.PromoCodeId, redemption.UserId }).IsUnique();
        builder.HasIndex(redemption => redemption.UserId);
        builder.HasIndex(redemption => redemption.OrganizationId);
        builder.HasOne(redemption => redemption.PromoCode)
            .WithMany(promoCode => promoCode.Redemptions)
            .HasForeignKey(redemption => redemption.PromoCodeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(redemption => redemption.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(redemption => redemption.Organization)
            .WithMany()
            .HasForeignKey(redemption => redemption.OrganizationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
