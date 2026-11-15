using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности сообщения в поддержку. </summary>
public class SupportMessageEntityTypeConfiguration : IEntityTypeConfiguration<SupportMessage>
{
    /// <summary> Настраивает отображение сущности сообщения в поддержку. </summary>
    public void Configure(EntityTypeBuilder<SupportMessage> builder)
    {
        builder.Property(x => x.TypeCode).HasMaxLength(40);
        builder.Property(x => x.StatusCode).HasMaxLength(40);
        builder.Property(x => x.UserId).HasMaxLength(450);
        builder.Property(x => x.Name).HasMaxLength(160);
        builder.Property(x => x.Email).HasMaxLength(254);
        builder.Property(x => x.Phone).HasMaxLength(64);
        builder.Property(x => x.Topic).HasMaxLength(180);
        builder.Property(x => x.Text).HasMaxLength(4000);
        builder.Property(x => x.SourcePage).HasMaxLength(400);
        builder.Property(x => x.IpHash).HasMaxLength(128);
        builder.Property(x => x.UserAgent).HasMaxLength(512);
        builder.Property(x => x.AdminComment).HasMaxLength(2000);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.StatusCode, x.CreatedAt });
        builder.HasIndex(x => x.IpHash);
        builder.HasIndex(x => x.Email);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.OrganizationId);
    }
}
