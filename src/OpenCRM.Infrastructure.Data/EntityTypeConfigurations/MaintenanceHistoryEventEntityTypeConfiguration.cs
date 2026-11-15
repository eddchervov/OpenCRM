using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация события истории обслуживания. </summary>
public class MaintenanceHistoryEventEntityTypeConfiguration : IEntityTypeConfiguration<MaintenanceHistoryEvent>
{
    /// <summary> Настраивает модель события истории обслуживания. </summary>
    public void Configure(EntityTypeBuilder<MaintenanceHistoryEvent> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.ActorDisplayName)
            .HasMaxLength(250);

        builder.Property(x => x.EventType)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnType("text");

        builder.Property(x => x.PayloadJson)
            .HasColumnType("jsonb");

        builder.HasIndex(x => new { x.MaintenanceId, x.CreatedAt });
        builder.HasIndex(x => new { x.VehicleId, x.CreatedAt });
        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAt });

        builder.HasOne(x => x.Maintenance)
            .WithMany(x => x.HistoryEvents)
            .HasForeignKey(x => x.MaintenanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
