using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

public sealed class OrganizationImportIdentityEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationImportIdentity>
{
    public void Configure(EntityTypeBuilder<OrganizationImportIdentity> builder)
    {
        builder.HasKey(identity => new { identity.OrganizationId, identity.SourceKey, identity.Kind, identity.ExternalId });
        builder.Property(identity => identity.SourceKey).HasMaxLength(100);
        builder.Property(identity => identity.Kind).HasMaxLength(30);
        builder.Property(identity => identity.ExternalId).HasMaxLength(100);
        builder.Property(identity => identity.Fingerprint).HasMaxLength(64);
        builder.ToTable("OrganizationImportIdentity", table => table.HasCheckConstraint("CK_OrganizationImportIdentity_Target",
            "(\"Kind\" = 'client' AND \"ClientId\" IS NOT NULL AND \"VehicleId\" IS NULL AND \"OrderId\" IS NULL) OR " +
            "(\"Kind\" = 'vehicle' AND \"ClientId\" IS NULL AND \"VehicleId\" IS NOT NULL AND \"OrderId\" IS NULL) OR " +
            "(\"Kind\" = 'order' AND \"ClientId\" IS NULL AND \"VehicleId\" IS NULL AND \"OrderId\" IS NOT NULL)"));
        builder.HasOne<OrganizationClient>().WithMany().HasForeignKey(identity => identity.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Vehicle>().WithMany().HasForeignKey(identity => identity.VehicleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Maintenance>().WithMany().HasForeignKey(identity => identity.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(identity => identity.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}
