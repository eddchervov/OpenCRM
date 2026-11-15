using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности сценария промокода. </summary>
public class PromoCodeScenarioEntityTypeConfiguration : IEntityTypeConfiguration<PromoCodeScenario>
{
    /// <summary> Настраивает отображение сущности сценария промокода. </summary>
    public void Configure(EntityTypeBuilder<PromoCodeScenario> builder)
    {
        builder.HasKey(promoCodeScenario => new { promoCodeScenario.PromoCodeId, promoCodeScenario.ScenarioCode });
        builder.Property(promoCodeScenario => promoCodeScenario.ScenarioCode).HasMaxLength(64).IsRequired();
        builder.HasOne(promoCodeScenario => promoCodeScenario.PromoCode)
            .WithMany(promoCode => promoCode.Scenarios)
            .HasForeignKey(promoCodeScenario => promoCodeScenario.PromoCodeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
