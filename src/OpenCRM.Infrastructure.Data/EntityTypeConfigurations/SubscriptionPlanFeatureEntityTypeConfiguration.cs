using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности возможности тарифного плана. </summary>
public class SubscriptionPlanFeatureEntityTypeConfiguration : IEntityTypeConfiguration<SubscriptionPlanFeature>
{
    /// <summary> Настраивает отображение сущности возможности тарифного плана. </summary>
    public void Configure(EntityTypeBuilder<SubscriptionPlanFeature> builder)
    {
        builder.HasIndex(x => new { x.SubscriptionPlanId, x.FeatureDefinitionId }).IsUnique();

        builder.HasOne(x => x.SubscriptionPlan)
            .WithMany(x => x.Features)
            .HasForeignKey(x => x.SubscriptionPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.FeatureDefinition)
            .WithMany(x => x.PlanFeatures)
            .HasForeignKey(x => x.FeatureDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

