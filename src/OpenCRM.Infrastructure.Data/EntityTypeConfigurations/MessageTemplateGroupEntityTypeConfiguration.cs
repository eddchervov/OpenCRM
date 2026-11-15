using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

public sealed class MessageTemplateGroupEntityTypeConfiguration : IEntityTypeConfiguration<MessageTemplateGroup>
{
    public void Configure(EntityTypeBuilder<MessageTemplateGroup> builder)
    {
        builder.Property(item => item.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(item => item.Name).HasMaxLength(100).IsRequired();
        builder.HasAlternateKey(item => new { item.OrganizationId, item.Id });
        builder.HasIndex(item => new { item.OrganizationId, item.SortOrder, item.Id });
        builder.HasOne(item => item.Organization)
            .WithMany()
            .HasForeignKey(item => item.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
