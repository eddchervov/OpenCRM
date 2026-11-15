using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Настраивает позицию прочтения сотрудника в диалоге. </summary>
public sealed class OrganizationConversationReadStateEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationConversationReadState>
{
    public void Configure(EntityTypeBuilder<OrganizationConversationReadState> builder)
    {
        builder.HasKey(item => new { item.ConversationId, item.UserId });
        builder.Property(item => item.UserId).HasMaxLength(450);
        builder.HasIndex(item => new { item.UserId, item.LastReadSequence });
        builder.HasOne(item => item.Conversation)
            .WithMany()
            .HasForeignKey(item => item.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
