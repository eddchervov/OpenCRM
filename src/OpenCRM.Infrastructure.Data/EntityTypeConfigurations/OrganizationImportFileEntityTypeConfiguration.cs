using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

public sealed class OrganizationImportFileEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationImportFile>
{
    public void Configure(EntityTypeBuilder<OrganizationImportFile> builder)
    {
        builder.HasKey(file => file.DraftId);
        builder.HasOne<OrganizationImportDraft>().WithMany().HasForeignKey(file => file.DraftId).OnDelete(DeleteBehavior.Cascade);
    }
}
