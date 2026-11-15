using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности интервала работы по обслуживанию. </summary>
public class MaintenanceWorkIntervalEntityTypeConfiguration : IEntityTypeConfiguration<MaintenanceWorkInterval>
{
    /// <summary> Настраивает отображение сущности интервала работы. </summary>
    public void Configure(EntityTypeBuilder<MaintenanceWorkInterval> builder)
    {
        builder.HasKey(interval => interval.Id);
        builder.HasIndex(interval => interval.OperationId)
            .IsUnique().HasFilter("\"EndedAt\" IS NULL");
        builder.HasOne(interval => interval.Operation).WithMany()
            .HasForeignKey(interval => interval.OperationId).OnDelete(DeleteBehavior.Restrict);
    }
}
