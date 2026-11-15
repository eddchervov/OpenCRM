using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация рекомендации повторного визита организации. </summary>
public sealed class OrganizationRepeatVisitEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationRepeatVisit>
{
    public void Configure(EntityTypeBuilder<OrganizationRepeatVisit> builder)
    {
        builder.Property(item => item.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(item => item.ServiceName).HasMaxLength(240).IsRequired();
        builder.Property(item => item.Reason).HasMaxLength(1000);
        builder.Property(item => item.ResponsibleUserId).HasMaxLength(450);
        builder.Property(item => item.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(item => item.Version).HasDefaultValue(1).IsConcurrencyToken();
        builder.Property(item => item.IsActive).HasDefaultValue(true);

        builder.HasIndex(item => new { item.OrganizationId, item.SuggestedVisitDate, item.UpdatedAt });
        builder.HasIndex(item => new { item.OrganizationId, item.ResponsibleUserId, item.ContactByUtc });
        builder.HasIndex(item => item.SourceMaintenanceId);
        builder.HasIndex(item => item.BookedMaintenanceId).IsUnique().HasFilter("\"BookedMaintenanceId\" IS NOT NULL");

        builder.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.OrganizationClient).WithMany().HasForeignKey(item => item.OrganizationClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.Vehicle).WithMany().HasForeignKey(item => item.VehicleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.SourceMaintenance).WithMany().HasForeignKey(item => item.SourceMaintenanceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.BookedMaintenance).WithMany().HasForeignKey(item => item.BookedMaintenanceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.ResponsibleUser).WithMany().HasForeignKey(item => item.ResponsibleUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(item => item.CreatedByUser).WithMany().HasForeignKey(item => item.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
