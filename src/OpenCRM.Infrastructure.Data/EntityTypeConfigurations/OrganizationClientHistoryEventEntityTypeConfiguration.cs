using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности события истории клиента организации. </summary>
public class OrganizationClientHistoryEventEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationClientHistoryEvent>
{
    /// <summary> Настраивает отображение сущности события истории клиента. </summary>
    public void Configure(EntityTypeBuilder<OrganizationClientHistoryEvent> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.ActorDisplayName)
            .HasMaxLength(250);

        builder.Property(x => x.EventType)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(1000);

        builder.Property(x => x.RelatedEntityType)
            .HasMaxLength(64);

        builder.HasIndex(x => new { x.OrganizationClientId, x.CreatedAt });
        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
        builder.HasIndex(x => new { x.RelatedEntityType, x.RelatedEntityId });

        builder.HasOne(x => x.OrganizationClient)
            .WithMany(x => x.HistoryEvents)
            .HasForeignKey(x => x.OrganizationClientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
