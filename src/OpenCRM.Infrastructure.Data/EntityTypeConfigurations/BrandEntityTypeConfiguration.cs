using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация бренда техники. </summary>
public class BrandEntityTypeConfiguration : IEntityTypeConfiguration<Brand>
{
    /// <summary> Настраивает модель бренда техники. </summary>
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.Property(x => x.Name).HasColumnType("citext");
        builder.Property(x => x.LogoKey).HasMaxLength(64);
        builder.HasIndex(x => new { x.Kind, x.Name }).IsUnique();
    }
}

