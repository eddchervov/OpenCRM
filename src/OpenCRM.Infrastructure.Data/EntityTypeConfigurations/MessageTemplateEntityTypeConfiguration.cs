using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

public sealed class MessageTemplateEntityTypeConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> builder)
    {
        builder.Property(item => item.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(item => item.Name).HasMaxLength(100).IsRequired();
        builder.Property(item => item.Body).HasMaxLength(4096).IsRequired();
        builder.HasIndex(item => new { item.OrganizationId, item.GroupId, item.IsArchived, item.Name });
        builder.HasOne(item => item.Group)
            .WithMany(group => group.Templates)
            .HasForeignKey(item => new { item.OrganizationId, item.GroupId })
            .HasPrincipalKey(group => new { group.OrganizationId, group.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
