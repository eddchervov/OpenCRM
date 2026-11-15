using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности аудита чеков НПД. </summary>
public sealed class AdminNpdReceiptAuditEntityTypeConfiguration : IEntityTypeConfiguration<AdminNpdReceiptAudit>
{
    /// <summary> Настраивает отображение сущности аудита чеков НПД. </summary>
    public void Configure(EntityTypeBuilder<AdminNpdReceiptAudit> builder)
    {
        builder.ToTable("NpdReceiptAudits", "admin");
        builder.Property(audit => audit.ChangedByUserId).HasMaxLength(450);
        builder.Property(audit => audit.Reason).HasMaxLength(500);
        builder.HasIndex(audit => new { audit.SubscriptionPaymentId, audit.ChangedAt });
        builder.HasOne<SubscriptionPayment>()
            .WithMany()
            .HasForeignKey(audit => audit.SubscriptionPaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
