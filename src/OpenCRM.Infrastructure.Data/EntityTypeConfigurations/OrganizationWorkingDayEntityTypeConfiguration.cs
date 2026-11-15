using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности рабочего дня организации. </summary>
public class OrganizationWorkingDayEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationWorkingDay>
{
    /// <summary> Настраивает отображение сущности рабочего дня организации. </summary>
    public void Configure(EntityTypeBuilder<OrganizationWorkingDay> builder)
    {
        builder.HasKey(day => new { day.OrganizationId, day.DayOfWeek });
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_OrganizationWorkingDays_DayAndTime",
            "\"DayOfWeek\" >= 0 AND \"DayOfWeek\" <= 6 AND \"StartTime\" < \"EndTime\""));
        builder.HasOne(day => day.Organization)
            .WithMany()
            .HasForeignKey(day => day.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(day => day.Breaks)
            .WithOne(value => value.WorkingDay)
            .HasForeignKey(value => new { value.OrganizationId, value.DayOfWeek })
            .HasPrincipalKey(day => new { day.OrganizationId, day.DayOfWeek })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
