using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

public sealed class OrganizationChannelConnectionEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationChannelConnection>
{
    public void Configure(EntityTypeBuilder<OrganizationChannelConnection> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_OrganizationChannelConnections_ProviderCode",
            "\"ProviderCode\" IN ('telegram', 'max')"));
        builder.Property(item => item.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(item => item.ProviderCode).HasMaxLength(16).IsRequired();
        builder.Property(item => item.ImmutableProviderIdentity).HasMaxLength(128).IsRequired();
        builder.Property(item => item.Username).HasMaxLength(128);
        builder.HasAlternateKey(item => new { item.OrganizationId, item.Id });
        builder.HasIndex(item => new { item.OrganizationId, item.ProviderCode, item.ImmutableProviderIdentity }).IsUnique();
        builder.HasIndex(item => new { item.OrganizationId, item.ProviderCode, item.IsCurrent })
            .IsUnique()
            .HasFilter("\"IsCurrent\" = TRUE");
        builder.HasOne(item => item.Organization)
            .WithMany()
            .HasForeignKey(item => item.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
