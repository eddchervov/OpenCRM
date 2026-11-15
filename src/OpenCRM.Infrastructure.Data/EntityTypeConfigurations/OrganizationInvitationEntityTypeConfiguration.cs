using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности приглашения в организацию. </summary>
public class OrganizationInvitationEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationInvitation>
{
    /// <summary> Настраивает отображение сущности приглашения в организацию. </summary>
    public void Configure(EntityTypeBuilder<OrganizationInvitation> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Email)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(x => x.NormalizedEmail)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(x => x.RoleCode)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.StatusCode)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.Token)
            .HasMaxLength(80)
            .IsRequired();

        builder.HasIndex(x => x.Token)
            .IsUnique();

        builder.HasIndex(x => new { x.OrganizationId, x.NormalizedEmail, x.StatusCode });

        builder.HasIndex(x => new { x.OrganizationId, x.NormalizedEmail })
            .IsUnique()
            .HasFilter("\"StatusCode\" = 'pending'");

        builder.HasOne(x => x.Organization)
            .WithMany(x => x.Invitations)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.InvitedByUser)
            .WithMany()
            .HasForeignKey(x => x.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AcceptedByUser)
            .WithMany()
            .HasForeignKey(x => x.AcceptedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
