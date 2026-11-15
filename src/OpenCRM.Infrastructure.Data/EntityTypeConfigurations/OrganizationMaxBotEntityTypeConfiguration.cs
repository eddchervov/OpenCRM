using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности бота организации в MAX. </summary>
public sealed class OrganizationMaxBotEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationMaxBot>
{
    /// <summary> Настраивает отображение сущности бота организации в MAX. </summary>
    public void Configure(EntityTypeBuilder<OrganizationMaxBot> builder)
    {
        builder.HasKey(x => x.OrganizationId);
        builder.Property(x => x.Username).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ImmutableProviderBotId).HasMaxLength(128);
        builder.HasIndex(x => x.Username).IsUnique();
        builder.HasIndex(x => x.ImmutableProviderBotId)
            .IsUnique()
            .HasFilter("\"ImmutableProviderBotId\" IS NOT NULL");
        builder.Property(x => x.ProtectedToken).IsRequired();
        builder.Property(x => x.ProtectedWebhookSecret).IsRequired();
        builder.HasOne(x => x.Organization)
            .WithOne()
            .HasForeignKey<OrganizationMaxBot>(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
