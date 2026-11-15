using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Настраивает сообщения диалогов организации. </summary>
public sealed class OrganizationConversationMessageEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationConversationMessage>
{
    public void Configure(EntityTypeBuilder<OrganizationConversationMessage> builder)
    {
        builder.Property(item => item.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(item => item.Direction).HasMaxLength(16).IsRequired();
        builder.Property(item => item.Status).HasMaxLength(16).IsRequired();
        builder.Property(item => item.Text).HasMaxLength(4096);
        builder.Property(item => item.ActorUserId).HasMaxLength(450);
        builder.Property(item => item.ActorDisplayName).HasMaxLength(300);
        builder.Property(item => item.ProviderMessageId).HasMaxLength(64);
        builder.Property(item => item.ProviderUpdateId).HasMaxLength(128);
        builder.Property(item => item.SenderExternalUserId).HasMaxLength(128);
        builder.Property(item => item.ErrorCode).HasMaxLength(100);
        builder.Property(item => item.ErrorMessage).HasMaxLength(1000);
        builder.Property(item => item.RetryAt);
        builder.Property(item => item.DeliveryAttemptCount).HasDefaultValue(0);

        builder.HasIndex(item => new { item.ConversationId, item.Sequence }).IsUnique();
        builder.HasIndex(item => new { item.ConversationId, item.Direction, item.Sequence });
        builder.HasIndex(item => new { item.ActorUserId, item.RequestId })
            .IsUnique()
            .HasFilter("\"RequestId\" IS NOT NULL");
        builder.HasIndex(item => new { item.ConversationId, item.ProviderMessageId })
            .IsUnique()
            .HasFilter("\"ProviderMessageId\" IS NOT NULL");
        builder.HasIndex(item => new { item.ConversationId, item.TelegramUpdateId })
            .IsUnique()
            .HasFilter("\"TelegramUpdateId\" IS NOT NULL");
        builder.HasIndex(item => new { item.ConversationId, item.ProviderUpdateId })
            .IsUnique()
            .HasFilter("\"ProviderUpdateId\" IS NOT NULL");
        builder.HasIndex(item => new { item.Status, item.RetryAt, item.CreatedAt })
            .HasFilter("\"Direction\" = 'outgoing' AND \"Status\" IN ('queued', 'failed')");
        builder.HasIndex(item => new { item.Status, item.LeaseExpiresAt, item.CreatedAt })
            .HasFilter("\"Direction\" = 'outgoing' AND \"Status\" IN ('queued', 'sending')");

        builder.HasOne(item => item.Conversation)
            .WithMany(conversation => conversation.Messages)
            .HasForeignKey(item => item.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(item => item.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(item => item.Maintenance)
            .WithMany()
            .HasForeignKey(item => item.MaintenanceId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<MessageTemplate>()
            .WithMany()
            .HasForeignKey(item => item.MessageTemplateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
