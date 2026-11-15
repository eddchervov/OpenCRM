using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности сотрудника организации. </summary>
public class OrganizationEmployeeEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationEmployee>
{
    /// <summary> Настраивает отображение сущности сотрудника организации. </summary>
    public void Configure(EntityTypeBuilder<OrganizationEmployee> builder)
    {
        builder.HasKey(employee => employee.Id);
        builder.Property(employee => employee.Name).HasMaxLength(250).IsRequired();
        builder.Property(employee => employee.Phone).HasMaxLength(50);
        builder.Property(employee => employee.Version).IsRowVersion();
        builder.HasIndex(employee => new { employee.OrganizationId, employee.IsActive });
        builder.HasIndex(employee => employee.MemberId).IsUnique();
        builder.HasOne(employee => employee.Organization).WithMany()
            .HasForeignKey(employee => employee.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(employee => employee.Member).WithMany()
            .HasForeignKey(employee => new { employee.OrganizationId, employee.MemberId })
            .HasPrincipalKey(member => new { member.OrganizationId, member.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
