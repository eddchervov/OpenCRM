using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности состояния задачи планировщика организации. </summary>
public class OrganizationSchedulerTaskStateEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationSchedulerTaskState>
{
    /// <summary> Настраивает отображение сущности состояния задачи планировщика. </summary>
    public void Configure(EntityTypeBuilder<OrganizationSchedulerTaskState> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.SourceCode)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.TriggerKey)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.StatusCode)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ProcessCode)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.WorkflowStageCode)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => x.VehicleId);
        builder.HasIndex(x => x.StatusCode);
        builder.HasIndex(x => new { x.OrganizationId, x.ProcessCode, x.WorkflowStageCode });
        builder.HasIndex(x => new { x.OrganizationId, x.TriggerKey })
            .IsUnique()
            .HasDatabaseName("IX_OrganizationSchedulerTaskStates_Organization_Trigger");

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Vehicle)
            .WithMany()
            .HasForeignKey(x => x.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Maintenance)
            .WithMany()
            .HasForeignKey(x => x.MaintenanceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
