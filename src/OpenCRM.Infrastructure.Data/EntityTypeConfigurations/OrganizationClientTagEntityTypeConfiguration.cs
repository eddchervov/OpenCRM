using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности тега клиента организации. </summary>
public class OrganizationClientTagEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationClientTag>
{
    /// <summary> Настраивает отображение сущности тега клиента организации. </summary>
    public void Configure(EntityTypeBuilder<OrganizationClientTag> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Name)
            .HasMaxLength(48)
            .IsRequired();

        builder.Property(x => x.NormalizedName)
            .HasMaxLength(48)
            .IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.NormalizedName })
            .IsUnique();

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
