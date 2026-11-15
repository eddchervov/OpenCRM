using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности Telegram-бота организации. </summary>
public sealed class OrganizationTelegramBotEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationTelegramBot>
{
    /// <summary> Настраивает отображение сущности Telegram-бота организации. </summary>
    public void Configure(EntityTypeBuilder<OrganizationTelegramBot> builder)
    {
        builder.HasKey(x => x.OrganizationId);
        builder.Property(x => x.Username).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.Username).IsUnique();
        builder.HasIndex(x => x.TelegramBotId).IsUnique().HasFilter("\"TelegramBotId\" IS NOT NULL");
        builder.Property(x => x.ProtectedToken).IsRequired();
        builder.Property(x => x.ProtectedWebhookSecret).IsRequired();
        builder.HasOne(x => x.Organization)
            .WithOne()
            .HasForeignKey<OrganizationTelegramBot>(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
