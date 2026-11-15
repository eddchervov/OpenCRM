using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Дедупликация входящих событий и сохранение безопасной истории финансовых действий. </summary>
public sealed class SubscriptionPaymentEventEntityTypeConfiguration : IEntityTypeConfiguration<SubscriptionPaymentEvent>
{
    public void Configure(EntityTypeBuilder<SubscriptionPaymentEvent> builder)
    {
        builder.ToTable("SubscriptionPaymentEvents");
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.MessageId).IsUnique().HasFilter("\"MessageId\" IS NOT NULL");
        builder.HasIndex(item => new { item.PurchaseId, item.CreatedAt });
        builder.HasIndex(item => new { item.ProcessedAt, item.NextAttemptAt });
        builder.Property(item => item.Kind).HasMaxLength(64);
        builder.Property(item => item.Code).HasMaxLength(128);
        builder.Property(item => item.MessageHash).HasMaxLength(64);
        builder.Property(item => item.Payload).HasColumnType("jsonb");
        builder.HasOne(item => item.Purchase).WithMany().HasForeignKey(item => item.PurchaseId).OnDelete(DeleteBehavior.Restrict);
    }
}
