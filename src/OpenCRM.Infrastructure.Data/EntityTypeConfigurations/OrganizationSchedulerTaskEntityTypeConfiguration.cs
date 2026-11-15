using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности задачи планировщика организации. </summary>
public class OrganizationSchedulerTaskEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationSchedulerTask>
{
    /// <summary> Настраивает отображение сущности задачи планировщика организации. </summary>
    public void Configure(EntityTypeBuilder<OrganizationSchedulerTask> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Comment)
            .HasMaxLength(2000);

        builder.Property(x => x.TypeCode)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.PriorityCode)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.StatusCode)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.ProcessCode)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.WorkflowStageCode)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.RecurrenceCode)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.ResponsibleUserId)
            .HasMaxLength(450);

        builder.Property(x => x.CreatedByUserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.IsDeleted, x.StartAt });
        builder.HasIndex(x => new { x.OrganizationId, x.StatusCode });
        builder.HasIndex(x => new { x.OrganizationId, x.TypeCode });
        builder.HasIndex(x => new { x.OrganizationId, x.PriorityCode });
        builder.HasIndex(x => new { x.OrganizationId, x.ResponsibleUserId });
        builder.HasIndex(x => new { x.OrganizationId, x.OrganizationClientId });
        builder.HasIndex(x => new { x.OrganizationId, x.VehicleId });
        builder.HasIndex(x => new { x.OrganizationId, x.LinkedDefectId });
        builder.HasIndex(x => new { x.OrganizationId, x.LinkedMaintenanceId });
        builder.HasIndex(x => new { x.OrganizationId, x.ReminderAt, x.ReminderSentAt });
        builder.HasIndex(x => new { x.OrganizationId, x.RecurrenceParentTaskId, x.StartAt });
        builder.HasIndex(x => new { x.OrganizationId, x.RecurrenceCode, x.RecurrenceGeneratedUntil });

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Vehicle)
            .WithMany()
            .HasForeignKey(x => x.VehicleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.OrganizationClient)
            .WithMany()
            .HasForeignKey(x => x.OrganizationClientId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.LinkedDefect)
            .WithMany()
            .HasForeignKey(x => x.LinkedDefectId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.LinkedMaintenance)
            .WithMany()
            .HasForeignKey(x => x.LinkedMaintenanceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.ResponsibleUser)
            .WithMany()
            .HasForeignKey(x => x.ResponsibleUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RecurrenceParentTask)
            .WithMany(x => x.RecurrenceChildren)
            .HasForeignKey(x => x.RecurrenceParentTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
