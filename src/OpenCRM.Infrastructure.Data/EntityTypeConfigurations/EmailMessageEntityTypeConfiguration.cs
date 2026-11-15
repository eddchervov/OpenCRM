using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности email-сообщения. </summary>
public class EmailMessageEntityTypeConfiguration : IEntityTypeConfiguration<EmailMessage>
{
    /// <summary> Настраивает отображение сущности email-сообщения. </summary>
    public void Configure(EntityTypeBuilder<EmailMessage> builder)
    {
        builder.Property(x => x.NotificationKey).HasMaxLength(200);
        builder.Property(x => x.NotificationStatus).HasMaxLength(40);
        builder.Property(x => x.TemplateKey).HasMaxLength(200);
        builder.HasIndex(x => x.NotificationKey).IsUnique();
        builder.HasIndex(x => x.NotificationCommandId);
        builder.HasIndex(x => x.NextDispatchAt);
        builder.HasIndex(x => x.TemplateKey);
        builder.Property(x => x.UserId).HasMaxLength(450);
        builder.Property(x => x.ToEmail).HasMaxLength(254);
        builder.Property(x => x.Subject).HasMaxLength(1000);
        builder.Property(x => x.StatusCode).HasMaxLength(40);
        builder.Property(x => x.Provider).HasMaxLength(80);
        builder.Property(x => x.ProviderMessageId).HasMaxLength(200);
        builder.Property(x => x.ProviderResponse).HasMaxLength(1000);
        builder.Property(x => x.LastEventCode).HasMaxLength(80);
        builder.Property(x => x.ErrorMessage).HasMaxLength(4000);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.SentAt);
        builder.HasIndex(x => x.ToEmail);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.ProviderMessageId);
        builder.HasIndex(x => x.LastEventAt);
        builder.HasIndex(x => new { x.StatusCode, x.CreatedAt });
    }
}

/// <summary> Конфигурация сущности события email-сообщения. </summary>
public class EmailMessageEventEntityTypeConfiguration : IEntityTypeConfiguration<EmailMessageEvent>
{
    /// <summary> Настраивает отображение сущности события email-сообщения. </summary>
    public void Configure(EntityTypeBuilder<EmailMessageEvent> builder)
    {
        builder.HasIndex(x => x.NotificationEventId).IsUnique();
        builder.Property(x => x.Provider).HasMaxLength(80);
        builder.Property(x => x.EventName).HasMaxLength(120);
        builder.Property(x => x.Status).HasMaxLength(80);
        builder.Property(x => x.Email).HasMaxLength(254);
        builder.Property(x => x.ProviderMessageId).HasMaxLength(200);
        builder.Property(x => x.CampaignId).HasMaxLength(120);
        builder.Property(x => x.Url).HasMaxLength(2000);
        builder.Property(x => x.IpAddress).HasMaxLength(100);
        builder.Property(x => x.UserAgent).HasMaxLength(1000);

        builder.HasOne(x => x.EmailMessage)
            .WithMany(x => x.Events)
            .HasForeignKey(x => x.EmailMessageId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.EmailMessageId);
        builder.HasIndex(x => x.ReceivedAt);
        builder.HasIndex(x => x.EventAt);
        builder.HasIndex(x => new { x.Provider, x.ProviderMessageId });
        builder.HasIndex(x => new { x.Status, x.EventAt });
        builder.HasIndex(x => new { x.Email, x.EventAt });
    }
}
