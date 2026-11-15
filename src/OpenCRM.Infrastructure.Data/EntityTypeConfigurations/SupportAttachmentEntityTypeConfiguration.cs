using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Связывает вложения с обращением и соответствующей записью переписки. </summary>
public sealed class SupportAttachmentEntityTypeConfiguration : IEntityTypeConfiguration<SupportAttachment>
{
    public void Configure(EntityTypeBuilder<SupportAttachment> builder)
    {
        builder.Property(x => x.FileName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.StorageReference).HasMaxLength(1024).IsRequired();
        builder.HasOne(x => x.SupportMessage)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.SupportMessageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Entry)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => new { x.EntryId, x.SupportMessageId })
            .HasPrincipalKey(x => new { x.Id, x.SupportMessageId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.SupportMessageId, x.EntryId, x.SortOrder });
        builder.ToTable(table => table.HasCheckConstraint("CK_SupportAttachments_SizeBytes", "\"SizeBytes\" > 0 AND \"SizeBytes\" <= 10485760"));
    }
}
