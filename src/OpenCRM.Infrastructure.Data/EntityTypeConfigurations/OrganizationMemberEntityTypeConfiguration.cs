using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности участника организации. </summary>
public class OrganizationMemberEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    /// <summary> Настраивает отображение сущности участника организации. </summary>
    public void Configure(EntityTypeBuilder<OrganizationMember> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.RoleCode)
            .HasMaxLength(40)
            .IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.UserId })
            .IsUnique();

        builder.HasIndex(x => x.RoleCode);

        builder.HasOne(x => x.Organization)
            .WithMany(x => x.Members)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany(x => x.OrganizationMemberships)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.InvitedByUser)
            .WithMany()
            .HasForeignKey(x => x.InvitedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
