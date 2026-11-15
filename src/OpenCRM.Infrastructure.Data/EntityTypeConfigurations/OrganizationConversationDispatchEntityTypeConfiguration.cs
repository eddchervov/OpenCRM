using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Настраивает транзакционную очередь публикации диалогов. </summary>
public sealed class OrganizationConversationDispatchEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationConversationDispatch>
{
    public void Configure(EntityTypeBuilder<OrganizationConversationDispatch> builder)
    {
        builder.Property(item => item.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(item => item.LastErrorCode).HasMaxLength(100);
        builder.HasIndex(item => item.MessageId).IsUnique();
        builder.HasIndex(item => new { item.PublishedAt, item.LeaseExpiresAt, item.CreatedAt });
        builder.HasIndex(item => item.NextReconcileAt)
            .HasFilter("\"NextReconcileAt\" IS NOT NULL");
        builder.HasOne(item => item.Message)
            .WithOne()
            .HasForeignKey<OrganizationConversationDispatch>(item => item.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
