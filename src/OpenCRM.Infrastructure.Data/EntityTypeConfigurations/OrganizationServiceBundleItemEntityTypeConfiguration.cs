using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности элемента набора услуг организации. </summary>
public sealed class OrganizationServiceBundleItemEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationServiceBundleItem>
{
    /// <summary> Настраивает отображение сущности элемента набора услуг. </summary>
    public void Configure(EntityTypeBuilder<OrganizationServiceBundleItem> builder)
    {
        builder.HasKey(item => new { item.BundleId, item.ServiceId });
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_OrganizationServiceBundleItems_KindAndPosition",
            "\"BundleKindCode\" = 'service_bundle' AND \"ServiceKindCode\" = 'service' AND \"Position\" >= 0"));
        builder.Property(item => item.BundleKindCode).HasMaxLength(40);
        builder.Property(item => item.ServiceKindCode).HasMaxLength(40);
        builder.HasIndex(item => new { item.BundleId, item.Position });
        builder.HasIndex(item => item.ServiceId);
        builder.HasOne(item => item.Bundle).WithMany(item => item.BundleItems)
            .HasForeignKey(item => new { item.BundleId, item.OrganizationId, item.BundleKindCode })
            .HasPrincipalKey(item => new { item.Id, item.OrganizationId, item.KindCode })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<OrganizationScenarioCatalogItem>().WithMany()
            .HasForeignKey(item => new { item.ServiceId, item.OrganizationId, item.ServiceKindCode })
            .HasPrincipalKey(item => new { item.Id, item.OrganizationId, item.KindCode })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
