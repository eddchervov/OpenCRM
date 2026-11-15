using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Настраивает метаданные вложений сообщений. </summary>
public sealed class OrganizationConversationAttachmentEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationConversationAttachment>
{
    public void Configure(EntityTypeBuilder<OrganizationConversationAttachment> builder)
    {
        builder.Property(item => item.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(item => item.Kind).HasMaxLength(32).IsRequired();
        builder.Property(item => item.FileName).HasMaxLength(255).IsRequired();
        builder.Property(item => item.ContentType).HasMaxLength(200).IsRequired();
        builder.Property(item => item.ProviderFileId).HasMaxLength(512);
        builder.Property(item => item.ProviderFileUniqueId).HasMaxLength(256);
        builder.Property(item => item.StorageReference).HasMaxLength(1024);
        builder.ToTable(table => table.HasCheckConstraint("CK_OrganizationConversationAttachments_SizeBytes", "\"SizeBytes\" >= 0"));

        builder.Property(item => item.UploadedByUserId).HasMaxLength(450);
        builder.HasIndex(item => new { item.MessageId, item.SortOrder }).IsUnique().HasFilter("\"MessageId\" IS NOT NULL");
        builder.HasIndex(item => new { item.OrganizationId, item.UploadedByUserId, item.MessageId });
        builder.HasIndex(item => new { item.ConversationId, item.MessageId, item.CreatedAt });
        builder.HasIndex(item => item.ProviderFileUniqueId);
        builder.HasOne<Organization>()
            .WithMany()
            .HasForeignKey(item => item.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Conversation)
            .WithMany(conversation => conversation.Attachments)
            .HasForeignKey(item => item.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(item => item.UploadedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(item => item.Message)
            .WithMany(message => message.Attachments)
            .HasForeignKey(item => item.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
