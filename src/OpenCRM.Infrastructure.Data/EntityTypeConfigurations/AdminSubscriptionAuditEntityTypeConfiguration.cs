using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности аудита подписок. </summary>
public sealed class AdminSubscriptionAuditEntityTypeConfiguration : IEntityTypeConfiguration<AdminSubscriptionAudit>
{
    /// <summary> Настраивает отображение сущности аудита подписок. </summary>
    public void Configure(EntityTypeBuilder<AdminSubscriptionAudit> builder)
    {
        builder.ToTable("SubscriptionAudits", "admin");
        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.Scope).HasMaxLength(32);
        builder.Property(audit => audit.ClientId).HasMaxLength(450);
        builder.Property(audit => audit.Action).HasMaxLength(64);
        builder.Property(audit => audit.ChangedByUserId).HasMaxLength(450);
        builder.Property(audit => audit.Reason).HasMaxLength(500);
        builder.HasIndex(audit => new { audit.Scope, audit.SubscriptionId, audit.ChangedAt });
        builder.HasIndex(audit => new { audit.Scope, audit.ClientId, audit.ChangedAt });
    }
}
