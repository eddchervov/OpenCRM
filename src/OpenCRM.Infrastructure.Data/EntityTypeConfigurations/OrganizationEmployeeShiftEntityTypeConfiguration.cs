using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности смены сотрудника организации. </summary>
public class OrganizationEmployeeShiftEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationEmployeeShift>
{
    /// <summary> Настраивает отображение сущности смены сотрудника. </summary>
    public void Configure(EntityTypeBuilder<OrganizationEmployeeShift> builder)
    {
        builder.HasKey(shift => shift.Id);
        builder.Property(shift => shift.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.Property(shift => shift.Version).IsRowVersion();
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_OrganizationEmployeeShifts_Plan",
            "\"PlanStartUtc\" < \"PlanEndUtc\" AND \"PlannedBreakMinutes\" >= 0"));
        builder.HasIndex(shift => new { shift.OrganizationId, shift.LocalStartDate, shift.EmployeeId });
        builder.HasIndex(shift => new { shift.OrganizationId, shift.EmployeeId, shift.PlanStartUtc, shift.PlanEndUtc });
        builder.HasOne(shift => shift.Organization).WithMany()
            .HasForeignKey(shift => shift.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(shift => shift.Employee).WithMany()
            .HasForeignKey(shift => new { shift.OrganizationId, shift.EmployeeId })
            .HasPrincipalKey(employee => new { employee.OrganizationId, employee.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

