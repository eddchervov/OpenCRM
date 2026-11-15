using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Хранение черновиков импорта отдельно от рабочих данных. </summary>
public sealed class OrganizationImportDraftEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationImportDraft>
{
    /// <summary> Настраивает ограничения и контроль конкурентного редактирования. </summary>
    public void Configure(EntityTypeBuilder<OrganizationImportDraft> builder)
    {
        builder.ToTable("OrganizationImportDrafts");
        builder.Property(draft => draft.SourceKey).HasMaxLength(100);
        builder.Property(draft => draft.UserId).HasMaxLength(450).IsRequired();
        builder.Property(draft => draft.FileName).HasMaxLength(260).IsRequired();
        builder.Property(draft => draft.Revision).IsConcurrencyToken();
        builder.Property(draft => draft.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(draft => draft.ResultJson).HasColumnType("jsonb");
        builder.Property(draft => draft.StorageVersion).HasDefaultValue(1);
        builder.Property(draft => draft.State).HasMaxLength(30).HasDefaultValue("ready");
        builder.Property(draft => draft.Error).HasMaxLength(2000);
        builder.Property(draft => draft.RequestedBy).HasMaxLength(450);
        builder.Property(draft => draft.ResumeState).HasMaxLength(30);
        builder.HasIndex(draft => draft.State);
        builder.HasIndex(draft => new { draft.OrganizationId, draft.UpdatedAt });
        builder.HasOne<Organization>().WithMany().HasForeignKey(draft => draft.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}
