using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Настраивает производную почасовую сводку услуг для финансового рейтинга. </summary>
public sealed class OrganizationServiceHourlyTotalEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationServiceHourlyTotal>
{
    public void Configure(EntityTypeBuilder<OrganizationServiceHourlyTotal> builder)
    {
        builder.Property(total => total.CurrencyCode).HasMaxLength(3).HasDefaultValue("RUB").IsRequired();
        builder.ToTable("OrganizationServiceHourlyTotals", table =>
        {
            table.HasComment("Производная сводка положительных строк услуг по организации, часу UTC и исходному имени. " +
                "C# обновляет её в транзакции обслуживания без триггеров; источник данных — заказы и строки работ.");
            table.HasCheckConstraint("CK_OrganizationServiceHourlyTotals_UtcHour", "\"UtcHour\" BETWEEN 0 AND 87649415");
            table.HasCheckConstraint("CK_OrganizationServiceHourlyTotals_Count", "\"Count\" > 0");
            table.HasCheckConstraint("CK_OrganizationServiceHourlyTotals_Amount", "\"Amount\" > 0");
            table.HasCheckConstraint("CK_OrganizationServiceHourlyTotals_NameHash",
                "\"NameHash\" = encode(sha256(convert_to(\"Name\", 'UTF8')), 'hex')");
        });
        builder.HasKey(total => new { total.OrganizationId, total.UtcHour, total.CurrencyCode, total.NameHash });
        builder.Property(total => total.NameHash).HasMaxLength(64).UseCollation("C");
        builder.Property(total => total.Name).IsRequired().UseCollation("C");
        builder.Property(total => total.Amount).HasPrecision(28, 2);
        builder.Property(total => total.FirstKey).IsRequired().HasMaxLength(103).UseCollation("C");
        builder.HasOne<Organization>().WithMany().HasForeignKey(total => total.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
