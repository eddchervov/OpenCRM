using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности пакета импорта CRM. </summary>
public sealed class CrmImportBatchEntityTypeConfiguration : IEntityTypeConfiguration<CrmImportBatch>
{
    /// <summary> Настраивает отображение сущности пакета импорта CRM. </summary>
    public void Configure(EntityTypeBuilder<CrmImportBatch> builder)
    {
        builder.Property(batch => batch.UserId).HasMaxLength(450).IsRequired();
        builder.Property(batch => batch.FileName).HasMaxLength(260).IsRequired();
        builder.Property(batch => batch.Fingerprint).HasMaxLength(64).IsRequired();
        builder.Property(batch => batch.RowsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(batch => batch.ResultJson).HasColumnType("jsonb");

        builder.HasIndex(batch => new { batch.OrganizationId, batch.Fingerprint })
            .IsUnique()
            .HasFilter("\"ImportedAt\" IS NOT NULL");
    }
}
