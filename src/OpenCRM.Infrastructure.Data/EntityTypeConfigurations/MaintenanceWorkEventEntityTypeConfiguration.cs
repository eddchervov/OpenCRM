using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности события работы по обслуживанию. </summary>
public class MaintenanceWorkEventEntityTypeConfiguration : IEntityTypeConfiguration<MaintenanceWorkEvent>
{
    /// <summary> Настраивает отображение сущности события работы. </summary>
    public void Configure(EntityTypeBuilder<MaintenanceWorkEvent> builder)
    {
        builder.HasKey(workEvent => workEvent.Id);
        builder.Property(workEvent => workEvent.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(workEvent => workEvent.Reason).HasMaxLength(1000);
        builder.HasIndex(workEvent => workEvent.CommandId).IsUnique();
        builder.HasIndex(workEvent => new { workEvent.OperationId, workEvent.CreatedAt });
        builder.HasOne(workEvent => workEvent.Operation).WithMany()
            .HasForeignKey(workEvent => workEvent.OperationId).OnDelete(DeleteBehavior.Restrict);
    }
}
