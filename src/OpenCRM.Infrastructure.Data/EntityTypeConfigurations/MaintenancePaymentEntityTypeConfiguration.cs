using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности платежа обслуживания. </summary>
public class MaintenancePaymentEntityTypeConfiguration : IEntityTypeConfiguration<MaintenancePayment>
{
    /// <summary> Настраивает отображение сущности платежа обслуживания. </summary>
    public void Configure(EntityTypeBuilder<MaintenancePayment> builder)
    {
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.MethodCode).HasMaxLength(16);
        builder.Property(x => x.PayerName).HasMaxLength(250);
        builder.Property(x => x.PayerPartyTypeCode).HasMaxLength(16);
        builder.Property(x => x.PayerInn).HasMaxLength(20);
        builder.Property(x => x.PayerKpp).HasMaxLength(20);
        builder.Property(x => x.PayerLegalAddress).HasMaxLength(500);
        builder.HasOne<OrganizationClient>().WithMany().HasForeignKey(x => x.PayerClientId).OnDelete(DeleteBehavior.SetNull);
        builder.Property(x => x.CreatedBy).HasMaxLength(256);
        builder.Property(x => x.CancelledBy).HasMaxLength(256);
        builder.Property(x => x.Comment).HasMaxLength(1000);
        builder.Property(x => x.CancellationReason).HasMaxLength(1000);
        builder.HasOne(x => x.Maintenance).WithMany().HasForeignKey(x => x.MaintenanceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.MaintenanceId, x.CreatedAt });
        builder.ToTable(table => table.HasCheckConstraint("CK_MaintenancePayment_Amount", "\"Amount\" > 0"));
    }
}
