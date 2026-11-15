using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Конфигурация сущности перерыва в рабочем дне организации. </summary>
public class OrganizationWorkingDayBreakEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationWorkingDayBreak>
{
    /// <summary> Настраивает отображение сущности перерыва рабочего дня. </summary>
    public void Configure(EntityTypeBuilder<OrganizationWorkingDayBreak> builder)
    {
        builder.HasKey(value => new { value.OrganizationId, value.DayOfWeek, value.StartTime });
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_OrganizationWorkingDayBreaks_Time",
            "\"StartTime\" < \"EndTime\""));
    }
}
