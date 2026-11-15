using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности аудита статусов платежей. </summary>
public sealed class AdminPaymentStatusAuditEntityTypeConfiguration : IEntityTypeConfiguration<AdminPaymentStatusAudit>
{
    /// <summary> Настраивает отображение сущности аудита статусов платежей. </summary>
    public void Configure(EntityTypeBuilder<AdminPaymentStatusAudit> builder)
    {
        builder.ToTable("PaymentStatusAudits", "admin");
        builder.Property(audit => audit.PreviousStatus).HasMaxLength(64);
        builder.Property(audit => audit.NewStatus).HasMaxLength(64);
        builder.Property(audit => audit.Source).HasMaxLength(64);
        builder.Property(audit => audit.Reason).HasMaxLength(500);
        builder.Property(audit => audit.ChangedByUserId).HasMaxLength(450);
        builder.HasIndex(audit => new { audit.PaymentId, audit.ChangedAt });
        builder.HasOne(audit => audit.Payment)
            .WithMany()
            .HasForeignKey(audit => audit.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
