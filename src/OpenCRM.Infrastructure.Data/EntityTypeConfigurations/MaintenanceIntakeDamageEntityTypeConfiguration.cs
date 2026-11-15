using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Настраивает хранение повреждений приёмки. </summary>
public sealed class MaintenanceIntakeDamageEntityTypeConfiguration : IEntityTypeConfiguration<MaintenanceIntakeDamage>
{
    public void Configure(EntityTypeBuilder<MaintenanceIntakeDamage> builder)
    {
        builder.ToTable("MaintenanceIntakeDamages", table =>
        {
            table.HasCheckConstraint("CK_MaintenanceIntakeDamages_Number", "\"Number\" > 0");
            table.HasCheckConstraint("CK_MaintenanceIntakeDamages_Position", "\"X\" >= 0 AND \"X\" <= 1 AND \"Y\" >= 0 AND \"Y\" <= 1");
            table.HasCheckConstraint("CK_MaintenanceIntakeDamages_View", "\"ViewCode\" IN ('front', 'rear', 'left', 'right')");
            table.HasCheckConstraint("CK_MaintenanceIntakeDamages_Type", "\"DamageType\" IN ('scratch', 'chip', 'dent', 'other')");
        });
        builder.HasKey(damage => damage.Id);
        builder.Property(damage => damage.Id).ValueGeneratedNever();
        builder.Property(damage => damage.ViewCode).HasMaxLength(16).IsRequired();
        builder.Property(damage => damage.DamageType).HasMaxLength(16).IsRequired();
        builder.Property(damage => damage.Description).HasMaxLength(4000);
        builder.HasIndex(damage => new { damage.MaintenanceId, damage.Number }).IsUnique();
        builder.HasAlternateKey(damage => new { damage.MaintenanceId, damage.Id });
        builder.HasOne(damage => damage.Maintenance).WithMany()
            .HasForeignKey(damage => damage.MaintenanceId).OnDelete(DeleteBehavior.Cascade);
    }
}
