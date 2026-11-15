using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация события истории ручного обращения. </summary>
public class OrganizationInquiryEventEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationInquiryEvent>
{
    public void Configure(EntityTypeBuilder<OrganizationInquiryEvent> builder)
    {
        builder.Property(item => item.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(item => item.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(item => item.EventType).HasMaxLength(64).IsRequired();
        builder.Property(item => item.Title).HasMaxLength(300).IsRequired();
        builder.Property(item => item.Description).HasMaxLength(1000);
        builder.Property(item => item.PreviousValue).HasMaxLength(1000);
        builder.Property(item => item.NewValue).HasMaxLength(1000);
        builder.HasIndex(item => new { item.OrganizationInquiryId, item.CreatedAt });
        builder.HasIndex(item => new { item.OrganizationId, item.CreatedAt });

        builder.HasOne(item => item.OrganizationInquiry).WithMany(inquiry => inquiry.Events)
            .HasForeignKey(item => item.OrganizationInquiryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.ActorUser).WithMany().HasForeignKey(item => item.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
