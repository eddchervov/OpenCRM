using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация предложений в каталог техники. </summary>
public class VehicleCatalogSuggestionEntityTypeConfiguration : IEntityTypeConfiguration<VehicleCatalogSuggestion>
{
    /// <summary> Настраивает модель предложений в каталог. </summary>
    public void Configure(EntityTypeBuilder<VehicleCatalogSuggestion> builder)
    {
        builder.Property(x => x.BrandName).HasColumnType("citext");
        builder.Property(x => x.ModelName).HasColumnType("citext");
        builder.HasIndex(x => new { x.Status, x.Kind });
        builder.HasIndex(x => new { x.Kind, x.BrandName, x.ModelName, x.Status })
            .IsUnique()
            .HasFilter("\"Status\" = 1");
    }
}

