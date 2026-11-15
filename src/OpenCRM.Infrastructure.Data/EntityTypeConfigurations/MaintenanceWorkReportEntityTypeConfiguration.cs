using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности отчёта о работе по обслуживанию. </summary>
public class MaintenanceWorkReportEntityTypeConfiguration : IEntityTypeConfiguration<MaintenanceWorkReport>
{
    /// <summary> Настраивает отображение сущности отчёта о работе. </summary>
    public void Configure(EntityTypeBuilder<MaintenanceWorkReport> builder)
    {
        builder.HasKey(report => report.Id);
        builder.Property(report => report.ApprovedPrice).HasPrecision(18, 2);
        builder.Property(report => report.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(report => report.Kind).HasMaxLength(20).IsRequired();
        builder.Property(report => report.Text).HasMaxLength(2000).IsRequired();
        builder.Property(report => report.Decision).HasMaxLength(20);
        builder.Property(report => report.DecisionReason).HasMaxLength(1000);
        builder.Property(report => report.DecidedByUserId).HasMaxLength(450);
        builder.HasIndex(report => report.CommandId).IsUnique();
        builder.HasIndex(report => new { report.MaintenanceId, report.DetailId, report.CreatedAt });
        builder.HasIndex(report => new { report.OrganizationId, report.Kind, report.Decision });
        builder.HasOne(report => report.Maintenance).WithMany()
            .HasForeignKey(report => report.MaintenanceId).OnDelete(DeleteBehavior.Restrict);
    }
}
