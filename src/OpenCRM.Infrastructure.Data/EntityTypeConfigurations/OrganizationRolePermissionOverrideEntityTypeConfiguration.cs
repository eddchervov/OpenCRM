using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности переопределения разрешений роли организации. </summary>
public class OrganizationRolePermissionOverrideEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationRolePermissionOverride>
{
    /// <summary> Настраивает отображение сущности переопределения разрешений роли. </summary>
    public void Configure(EntityTypeBuilder<OrganizationRolePermissionOverride> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.RoleCode)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.PermissionCode)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.UpdatedByUserId)
            .HasMaxLength(450);

        builder.HasIndex(x => new { x.OrganizationId, x.RoleCode, x.PermissionCode })
            .IsUnique();

        builder.HasIndex(x => x.RoleCode);
        builder.HasIndex(x => x.PermissionCode);

        builder.HasOne(x => x.Organization)
            .WithMany(x => x.RolePermissionOverrides)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
