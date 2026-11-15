using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация ручного обращения организации. </summary>
public class OrganizationInquiryEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationInquiry>
{
    public void Configure(EntityTypeBuilder<OrganizationInquiry> builder)
    {
        builder.Property(item => item.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(item => item.ContactName).HasMaxLength(160).IsRequired();
        builder.Property(item => item.Phone).HasMaxLength(40);
        builder.Property(item => item.NormalizedPhone).HasMaxLength(40);
        builder.Property(item => item.SourceCode).HasMaxLength(40);
        builder.Property(item => item.Interest).HasMaxLength(4000).IsRequired();
        builder.Property(item => item.VehicleDescription).HasMaxLength(500);
        builder.Property(item => item.StageCode).HasMaxLength(32).IsRequired();
        builder.Property(item => item.ResponsibleUserId).HasMaxLength(450);
        builder.Property(item => item.NextActionText).HasMaxLength(500);
        builder.Property(item => item.CloseReason).HasMaxLength(1000);
        builder.Property(item => item.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(item => item.Version).IsConcurrencyToken();

        builder.HasIndex(item => new { item.OrganizationId, item.StageCode, item.UpdatedAt });
        builder.HasIndex(item => new { item.OrganizationId, item.ResponsibleUserId, item.NextActionAt });
        builder.HasIndex(item => new { item.OrganizationId, item.NormalizedPhone });
        builder.HasIndex(item => item.MaintenanceId).IsUnique().HasFilter("\"MaintenanceId\" IS NOT NULL");

        builder.HasOne(item => item.Organization).WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.OrganizationClient).WithMany().HasForeignKey(item => item.OrganizationClientId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(item => item.Vehicle).WithMany().HasForeignKey(item => item.VehicleId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(item => item.Maintenance).WithMany().HasForeignKey(item => item.MaintenanceId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(item => item.ResponsibleUser).WithMany().HasForeignKey(item => item.ResponsibleUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(item => item.CreatedByUser).WithMany().HasForeignKey(item => item.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
