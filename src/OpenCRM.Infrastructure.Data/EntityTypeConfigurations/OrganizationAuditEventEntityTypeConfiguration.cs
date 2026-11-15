using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности события аудита организации. </summary>
public class OrganizationAuditEventEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationAuditEvent>
{
    /// <summary> Настраивает отображение сущности события аудита организации. </summary>
    public void Configure(EntityTypeBuilder<OrganizationAuditEvent> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.ActorUserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(x => x.EventType)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasColumnType("text");

        builder.Property(x => x.TargetType)
            .HasMaxLength(80);

        builder.Property(x => x.TargetId)
            .HasMaxLength(120);

        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
        builder.HasIndex(x => x.EventType);
        builder.HasIndex(x => x.ActorUserId);

        builder.HasOne(x => x.Organization)
            .WithMany(x => x.AuditEvents)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
