using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности строки работы или услуги обслуживания. </summary>
public class MaintenanceDetailEntityTypeConfiguration : IEntityTypeConfiguration<MaintenanceDetail>
{
    /// <summary> Настраивает отображение сущности строки обслуживания. </summary>
    public void Configure(EntityTypeBuilder<MaintenanceDetail> builder)
    {
        builder.ToTable("MaintenanceDetails", table => table.HasCheckConstraint(
            "CK_MaintenanceDetails_Position",
            "\"Position\" >= 0"));
        builder.HasKey(detail => new { detail.MaintenanceId, detail.DetailId });
        builder.Property(detail => detail.DetailId).ValueGeneratedNever();
        builder.Property(detail => detail.Position).IsRequired();
        builder.Property(detail => detail.Price).HasPrecision(18, 2);
        builder.HasIndex(detail => new { detail.MaintenanceId, detail.Position })
            .IncludeProperties(detail => new { detail.Price, detail.DetailId });
    }
}
