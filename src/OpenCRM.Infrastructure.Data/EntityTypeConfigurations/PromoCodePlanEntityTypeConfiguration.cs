using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности плана промокода. </summary>
public class PromoCodePlanEntityTypeConfiguration : IEntityTypeConfiguration<PromoCodePlan>
{
    /// <summary> Настраивает отображение сущности плана промокода. </summary>
    public void Configure(EntityTypeBuilder<PromoCodePlan> builder)
    {
        builder.HasKey(promoCodePlan => new { promoCodePlan.PromoCodeId, promoCodePlan.SubscriptionPlanId });
        builder.HasOne(promoCodePlan => promoCodePlan.PromoCode)
            .WithMany(promoCode => promoCode.Plans)
            .HasForeignKey(promoCodePlan => promoCodePlan.PromoCodeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(promoCodePlan => promoCodePlan.SubscriptionPlan)
            .WithMany()
            .HasForeignKey(promoCodePlan => promoCodePlan.SubscriptionPlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
