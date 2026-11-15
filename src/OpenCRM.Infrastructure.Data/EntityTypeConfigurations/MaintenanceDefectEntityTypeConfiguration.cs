using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности дефекта обслуживания. </summary>
public class MaintenanceDefectEntityTypeConfiguration : IEntityTypeConfiguration<MaintenanceDefect>
{
    /// <summary> Настраивает отображение сущности дефекта обслуживания. </summary>
    public void Configure(EntityTypeBuilder<MaintenanceDefect> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.HasIndex(x => x.MaintenanceId);
        builder.HasIndex(x => x.VehicleDefectId);
        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => new { x.MaintenanceId, x.VehicleDefectId })
            .IsUnique();

        builder.HasOne(x => x.Maintenance)
            .WithMany(x => x.LinkedDefects)
            .HasForeignKey(x => x.MaintenanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.VehicleDefect)
            .WithMany(x => x.MaintenanceLinks)
            .HasForeignKey(x => x.VehicleDefectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
