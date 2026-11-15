using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности определения возможности. </summary>
public class FeatureDefinitionEntityTypeConfiguration : IEntityTypeConfiguration<FeatureDefinition>
{
    /// <summary> Настраивает отображение сущности определения возможности. </summary>
    public void Configure(EntityTypeBuilder<FeatureDefinition> builder)
    {
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Category).HasMaxLength(100);
        builder.Property(x => x.ScopeCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AudienceCode).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

