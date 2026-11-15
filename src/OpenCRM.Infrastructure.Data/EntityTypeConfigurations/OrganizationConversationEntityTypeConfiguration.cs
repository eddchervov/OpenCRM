using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Настраивает диалоги организации в PostgreSQL. </summary>
public sealed class OrganizationConversationEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationConversation>
{
    public void Configure(EntityTypeBuilder<OrganizationConversation> builder)
    {
        builder.Property(item => item.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(item => item.BotUsername).HasMaxLength(100).IsRequired();
        builder.Property(item => item.TelegramDisplayName).HasMaxLength(300);
        builder.Property(item => item.TelegramUsername).HasMaxLength(100);
        builder.Property(item => item.LastMessageText).HasMaxLength(4096);
        builder.Property(item => item.IsChatActive).HasDefaultValue(true);
        builder.Property(item => item.ExternalConversationId).HasMaxLength(128);
        builder.Property(item => item.DeliveryAddress).HasMaxLength(128);

        builder.HasIndex(item => new { item.OrganizationId, item.BotId, item.TelegramChatId }).IsUnique();
        builder.HasIndex(item => new { item.ConnectionId, item.ExternalConversationId })
            .IsUnique()
            .HasFilter("\"ConnectionId\" IS NOT NULL AND \"ExternalConversationId\" IS NOT NULL");
        builder.HasIndex(item => new { item.OrganizationId, item.LastMessageAt, item.Id });
        builder.HasIndex(item => new { item.OrganizationId, item.OrganizationClientId, item.LastMessageAt });

        builder.HasOne(item => item.Organization)
            .WithMany()
            .HasForeignKey(item => item.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.OrganizationClient)
            .WithMany()
            .HasForeignKey(item => item.OrganizationClientId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(item => item.Connection)
            .WithMany(connection => connection.Conversations)
            .HasForeignKey(item => new { item.OrganizationId, item.ConnectionId })
            .HasPrincipalKey(connection => new { connection.OrganizationId, connection.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
