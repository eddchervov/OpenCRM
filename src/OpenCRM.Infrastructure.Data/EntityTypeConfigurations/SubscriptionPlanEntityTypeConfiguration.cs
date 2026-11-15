using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности тарифного плана подписки. </summary>
public class SubscriptionPlanEntityTypeConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    /// <summary> Настраивает отображение сущности тарифного плана. </summary>
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.Property(x => x.Price)
            .HasPrecision(18, 2);

        builder.Property(x => x.DiscountPrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.PlanTypeCode)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(x => x.PlanTypeCode);
    }
}
