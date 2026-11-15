using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сформированного документа обслуживания. </summary>
public class MaintenanceDocumentEntityTypeConfiguration : IEntityTypeConfiguration<MaintenanceDocument>
{
    /// <summary> Настраивает модель сформированного документа обслуживания. </summary>
    public void Configure(EntityTypeBuilder<MaintenanceDocument> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Kind)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Format)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(x => x.FileName)
            .HasMaxLength(260)
            .IsRequired();

        builder.Property(x => x.StoragePath)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.ContentType)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.CreatedByDisplayName)
            .HasMaxLength(250);

        builder.HasIndex(x => new { x.MaintenanceId, x.CreatedAt });
        builder.HasIndex(x => new { x.MaintenanceId, x.Kind, x.Format, x.IncludePhotos, x.Version })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
        builder.HasIndex(x => x.StoragePath);

        builder.HasOne(x => x.Maintenance)
            .WithMany(x => x.Documents)
            .HasForeignKey(x => x.MaintenanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
