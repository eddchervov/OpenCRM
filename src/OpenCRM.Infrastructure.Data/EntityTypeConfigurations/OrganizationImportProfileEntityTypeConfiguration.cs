using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

public sealed class OrganizationImportProfileEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationImportProfile>
{
    public void Configure(EntityTypeBuilder<OrganizationImportProfile> builder)
    {
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.UserId).HasMaxLength(450);
        builder.Property(profile => profile.Name).HasMaxLength(100);
        builder.Property(profile => profile.SourceKey).HasMaxLength(100);
        builder.Property(profile => profile.SettingsJson).HasColumnType("jsonb");
        builder.HasIndex(profile => new { profile.OrganizationId, profile.UserId, profile.Name }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(profile => profile.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}
