using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности email-рассылки. </summary>
public class EmailCampaignEntityTypeConfiguration : IEntityTypeConfiguration<EmailCampaign>
{
    /// <summary> Настраивает отображение сущности email-рассылки. </summary>
    public void Configure(EntityTypeBuilder<EmailCampaign> builder)
    {
        builder.Property(x => x.Subject).HasMaxLength(300);
        builder.Property(x => x.StatusCode).HasMaxLength(40);
        builder.Property(x => x.TargetMode).HasMaxLength(40);
        builder.Property(x => x.TargetUserId).HasMaxLength(450);
        builder.Property(x => x.TargetOrganizationTypeCode).HasMaxLength(80);
        builder.Property(x => x.CreatedByUserId).HasMaxLength(450);
        builder.Property(x => x.LastError).HasMaxLength(4000);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.StartAt);
        builder.HasIndex(x => new { x.StatusCode, x.StartAt });
        builder.HasIndex(x => x.CreatedByUserId);
    }
}

/// <summary> Конфигурация сущности получателя email-рассылки. </summary>
public class EmailCampaignRecipientEntityTypeConfiguration : IEntityTypeConfiguration<EmailCampaignRecipient>
{
    /// <summary> Настраивает отображение сущности получателя email-рассылки. </summary>
    public void Configure(EntityTypeBuilder<EmailCampaignRecipient> builder)
    {
        builder.Property(x => x.UserId).HasMaxLength(450);
        builder.Property(x => x.Email).HasMaxLength(254);
        builder.Property(x => x.DisplayName).HasMaxLength(300);
        builder.Property(x => x.StatusCode).HasMaxLength(40);
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);

        builder.HasOne(x => x.EmailCampaign)
            .WithMany(x => x.Recipients)
            .HasForeignKey(x => x.EmailCampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.EmailCampaignId);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.EmailCampaignId, x.StatusCode, x.CreatedAt });
        builder.HasIndex(x => new { x.EmailCampaignId, x.Email }).IsUnique();
    }
}
