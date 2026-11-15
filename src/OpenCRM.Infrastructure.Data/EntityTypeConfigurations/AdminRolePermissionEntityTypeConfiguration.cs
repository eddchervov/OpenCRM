using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности разрешений роли администратора. </summary>
public sealed class AdminRolePermissionEntityTypeConfiguration : IEntityTypeConfiguration<AdminRolePermission>
{
    /// <summary> Настраивает отображение сущности разрешений роли администратора. </summary>
    public void Configure(EntityTypeBuilder<AdminRolePermission> builder)
    {
        builder.ToTable("RolePermissions", "admin");
        builder.HasKey(permission => new { permission.RoleId, permission.PermissionCode });

        builder.Property(permission => permission.RoleId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(permission => permission.PermissionCode)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(permission => permission.CreatedByUserId)
            .HasMaxLength(450);

        builder.HasIndex(permission => permission.PermissionCode);

        builder.HasOne<IdentityRole>()
            .WithMany()
            .HasForeignKey(permission => permission.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
