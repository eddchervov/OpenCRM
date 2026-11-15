using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Хранит подготовку построчно с атомарным журналом применения. </summary>
public sealed class OrganizationImportRowEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationImportRow>
{
    public void Configure(EntityTypeBuilder<OrganizationImportRow> builder)
    {
        builder.HasKey(row => new { row.DraftId, row.Id });
        builder.Property(row => row.Id).HasMaxLength(180);
        builder.Property(row => row.Kind).HasMaxLength(30);
        builder.Property(row => row.Status).HasMaxLength(30);
        builder.Property(row => row.PayloadJson).HasColumnType("jsonb");
        builder.Property(row => row.ExternalId).HasMaxLength(100);
        builder.Property(row => row.ParentExternalId).HasMaxLength(100);
        builder.HasIndex(row => new { row.DraftId, row.Kind, row.ExternalId });
        builder.HasIndex(row => new { row.DraftId, row.ParentExternalId });
        builder.HasIndex(row => new { row.DraftId, row.Position }).IsUnique();
        builder.HasIndex(row => new { row.DraftId, row.Kind, row.Status });
        builder.HasOne<OrganizationImportDraft>().WithMany().HasForeignKey(row => row.DraftId).OnDelete(DeleteBehavior.Cascade);
    }
}
