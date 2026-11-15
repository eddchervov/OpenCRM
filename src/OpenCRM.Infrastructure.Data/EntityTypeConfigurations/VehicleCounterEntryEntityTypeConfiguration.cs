using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности записи счётчика техники. </summary>
public class VehicleCounterEntryEntityTypeConfiguration : IEntityTypeConfiguration<VehicleCounterEntry>
{
    /// <summary> Настраивает отображение сущности записи счётчика техники. </summary>
    public void Configure(EntityTypeBuilder<VehicleCounterEntry> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.SourceType)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Comment)
            .HasMaxLength(600);

        builder.HasIndex(x => x.VehicleId);
        builder.HasIndex(x => x.RecordedAt);
        builder.HasIndex(x => x.MaintenanceId);
        builder.HasIndex(x => new { x.VehicleId, x.RecordedAt, x.IsDeleted });

        builder.HasOne(x => x.Vehicle)
            .WithMany(x => x.CounterEntries)
            .HasForeignKey(x => x.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Maintenance)
            .WithMany(x => x.CounterEntries)
            .HasForeignKey(x => x.MaintenanceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
