using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности клиента организации. </summary>
public class OrganizationClientEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationClient>
{
    /// <summary> Настраивает отображение сущности клиента организации. </summary>
    public void Configure(EntityTypeBuilder<OrganizationClient> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Name)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(x => x.LastName).HasMaxLength(60);
        builder.Property(x => x.FirstName).HasMaxLength(60);
        builder.Property(x => x.MiddleName).HasMaxLength(60);
        builder.Property(x => x.CompanyTypeCode).HasMaxLength(16);

        builder.Property(x => x.Phone)
            .HasMaxLength(40);

        builder.Property(x => x.PartyTypeCode)
            .HasMaxLength(16)
            .HasDefaultValue("person");

        builder.Property(x => x.Inn)
            .HasMaxLength(20);

        builder.Property(x => x.Kpp)
            .HasMaxLength(20);

        builder.Property(x => x.LegalAddress)
            .HasMaxLength(500);

        builder.Property(x => x.Comment)
            .HasMaxLength(4000);

        builder.Property(x => x.StatusCode)
            .HasMaxLength(32)
            .HasDefaultValue("active")
            .IsRequired();

        builder.Property(x => x.ResponsibleUserId)
            .HasMaxLength(450);

        builder.Property(x => x.MaxLinkTokenHash)
            .HasMaxLength(64);

        builder.HasIndex(x => x.MaxLinkTokenHash).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.MaxUserId });

        builder.Property(x => x.TelegramLinkTokenHash)
            .HasMaxLength(64);

        builder.HasIndex(x => x.TelegramLinkTokenHash).IsUnique();
        builder.HasIndex(x => new { x.OrganizationId, x.TelegramChatId }).IsUnique();

        builder.HasIndex(x => x.OrganizationId);
        builder.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        builder.HasIndex(x => new { x.OrganizationId, x.Name });
        builder.HasIndex(x => new { x.OrganizationId, x.StatusCode });
        builder.HasIndex(x => new { x.OrganizationId, x.ResponsibleUserId });

        builder.HasOne(x => x.Organization)
            .WithMany(x => x.Clients)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
