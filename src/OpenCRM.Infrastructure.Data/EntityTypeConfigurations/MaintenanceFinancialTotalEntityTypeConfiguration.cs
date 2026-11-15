using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary> Настраивает финансовую проекцию обслуживания. </summary>
public sealed class MaintenanceFinancialTotalEntityTypeConfiguration : IEntityTypeConfiguration<MaintenanceFinancialTotal>
{
    /// <summary> Настраивает точность сумм, связи и индексы чтения сводки. </summary>
    public void Configure(EntityTypeBuilder<MaintenanceFinancialTotal> builder)
    {
        builder.ToTable("MaintenanceFinancialTotals", table => table.HasComment(
            "Готовые финансовые показатели одного обслуживания. C# обновляет проекцию в транзакции исходного заказа и работ; " +
            "оплаты, заказ и строки работ остаются источником данных. Финансовые триггеры не используются."));
        builder.HasKey(total => total.MaintenanceId);
        builder.Property(total => total.MaintenanceId).ValueGeneratedNever();
        builder.Property(total => total.GrossAmount).HasPrecision(28, 2);
        builder.Property(total => total.NetAmount).HasPrecision(28, 2);
        builder.Property(total => total.PaidAmount).HasPrecision(28, 2);
        builder.Property(total => total.DebtAmount).HasPrecision(28, 2);
        builder.Property(total => total.DiscountAmount).HasPrecision(28, 2);
        builder.Property(total => total.MaterialCostAmount).HasPrecision(28, 2);
        builder.Property(total => total.LaborCostAmount).HasPrecision(28, 2);
        builder.Property(total => total.ProfitAmount).HasPrecision(28, 2);
        builder.Property(total => total.ClientName).HasMaxLength(250).UseCollation("C");
        builder.Property(total => total.FirstKey).HasMaxLength(55).UseCollation("C");
        builder.HasOne<Maintenance>().WithOne().HasForeignKey<MaintenanceFinancialTotal>(total => total.MaintenanceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(total => total.OrganizationId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(total => new { total.OrganizationId, total.EffectiveDate })
            .HasDatabaseName("IX_MaintenanceFinancialTotals_FinanceDate")
            .HasFilter("\"IsVisible\"")
            .IncludeProperties(total => new
            {
                total.MaintenanceId, total.UtcDay, total.FirstKey, total.GrossAmount, total.NetAmount, total.PaidAmount, total.DebtAmount,
                total.DiscountAmount, total.MaterialCostAmount, total.LaborCostAmount, total.ProfitAmount, total.IsAccruable
            });
        builder.HasIndex(total => new { total.OrganizationId, total.EffectiveDate }, "IX_MaintenanceFinancialTotals_DebtDate")
            .HasFilter("\"IsVisible\" AND \"DebtAmount\" > 0")
            .IncludeProperties(total => new { total.ClientId, total.ClientName, total.DebtAmount, total.FirstKey, total.IsAccruable });
        builder.HasIndex(total => new { total.OrganizationId, total.UtcDay, total.MaintenanceId })
            .HasDatabaseName("IX_MaintenanceFinancialTotals_OrderDay")
            .IsDescending(false, true, true)
            .HasFilter("\"IsVisible\"")
            .IncludeProperties(total => total.EffectiveDate);
    }
}