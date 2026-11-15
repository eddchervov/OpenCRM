using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности операции работы по обслуживанию. </summary>
public class MaintenanceWorkOperationEntityTypeConfiguration : IEntityTypeConfiguration<MaintenanceWorkOperation>
{
    /// <summary> Настраивает отображение сущности операции работы. </summary>
    public void Configure(EntityTypeBuilder<MaintenanceWorkOperation> builder)
    {
        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Version).IsRowVersion();
        builder.HasIndex(operation => new { operation.OrganizationId, operation.EmployeeId, operation.Status });
        builder.HasOne(operation => operation.Maintenance).WithMany()
            .HasForeignKey(operation => operation.MaintenanceId).OnDelete(DeleteBehavior.Restrict);
    }
}
