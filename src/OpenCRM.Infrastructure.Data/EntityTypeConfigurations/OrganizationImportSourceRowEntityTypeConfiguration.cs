using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

public sealed class OrganizationImportSourceRowEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationImportSourceRow>
{
    public void Configure(EntityTypeBuilder<OrganizationImportSourceRow> builder)
    {
        builder.HasKey(row => new { row.DraftId, row.TableId, row.Number });
        builder.Property(row => row.TableId).HasMaxLength(100);
        builder.Property(row => row.ValuesJson).HasColumnType("jsonb");
        builder.HasOne<OrganizationImportDraft>().WithMany().HasForeignKey(row => row.DraftId).OnDelete(DeleteBehavior.Cascade);
    }
}
