using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности подписки организации. </summary>
public class OrganizationSubscriptionEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationSubscription>
{
    /// <summary> Настраивает отображение сущности подписки организации. </summary>
    public void Configure(EntityTypeBuilder<OrganizationSubscription> builder)
    {
        builder.HasOne(subscription => subscription.SubscriptionPurchase).WithMany()
            .HasForeignKey(subscription => subscription.SubscriptionPurchaseId).OnDelete(DeleteBehavior.Restrict);

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => x.EndDate);
        builder.Property(x => x.SourceCode).HasMaxLength(16).HasDefaultValue("unknown");

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.SubscriptionPlan)
            .WithMany()
            .HasForeignKey(x => x.SubscriptionPlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
