using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности элемента каталога сценариев организации. </summary>
public class OrganizationScenarioCatalogItemEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationScenarioCatalogItem>
{
    /// <summary> Настраивает отображение сущности элемента каталога сценариев. </summary>
    public void Configure(EntityTypeBuilder<OrganizationScenarioCatalogItem> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.ScenarioCode)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.KindCode)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.Code)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(x => x.HasBeenUsed).HasDefaultValue(false);

        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.HasAlternateKey(item => new { item.Id, item.OrganizationId, item.KindCode });

        builder.Property(x => x.Description)
            .HasMaxLength(1000);

        builder.Property(x => x.DocumentKindCode)
            .HasMaxLength(60);

        builder.HasIndex(x => new { x.OrganizationId, x.KindCode, x.Code })
            .IsUnique();

        builder.HasIndex(x => new { x.OrganizationId, x.KindCode, x.IsActive, x.SortOrder });

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
