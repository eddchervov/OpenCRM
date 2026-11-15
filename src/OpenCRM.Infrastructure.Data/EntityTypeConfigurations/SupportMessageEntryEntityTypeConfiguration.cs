using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности записи в переписке поддержки. </summary>
public class SupportMessageEntryEntityTypeConfiguration : IEntityTypeConfiguration<SupportMessageEntry>
{
    /// <summary> Настраивает отображение сущности записи переписки поддержки. </summary>
    public void Configure(EntityTypeBuilder<SupportMessageEntry> builder)
    {
        builder.Property(x => x.AuthorUserId).HasMaxLength(450);
        builder.Property(x => x.Kind).HasMaxLength(20);
        builder.Property(x => x.Text).HasMaxLength(4000);

        builder.HasOne(x => x.SupportMessage)
            .WithMany(x => x.Entries)
            .HasForeignKey(x => x.SupportMessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.SupportMessageId, x.CreatedAt });
    }
}
