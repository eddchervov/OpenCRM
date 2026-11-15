using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация модели техники. </summary>
public class ModelEntityTypeConfiguration : IEntityTypeConfiguration<Model>
{
    /// <summary> Настраивает модель справочника техники. </summary>
    public void Configure(EntityTypeBuilder<Model> builder)
    {
        builder.Property(x => x.Name).HasColumnType("citext");
        builder.HasIndex(x => new { x.BrandId, x.Kind, x.Name }).IsUnique();
    }
}

