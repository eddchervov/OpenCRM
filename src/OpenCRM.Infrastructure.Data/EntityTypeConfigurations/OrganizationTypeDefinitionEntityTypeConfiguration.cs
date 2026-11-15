using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Отображение каталога поддерживаемых типов организаций. </summary>
public sealed class OrganizationTypeDefinitionEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationTypeDefinition>
{
    public void Configure(EntityTypeBuilder<OrganizationTypeDefinition> builder)
    {
        builder.Property(x => x.Code).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(800).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.SortOrder);
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_OrganizationTypeDefinitions_SupportedCode",
            "\"IsSupported\" = (\"Code\" IN ('detailing', 'service-station', 'fleet'))"));
    }
}
