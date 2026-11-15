using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary>
/// EF-конфигурация истории движений склада материалов организации.
/// </summary>
public class OrganizationMaterialMovementEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationMaterialMovement>
{
    /// <summary>
    /// Настраивает таблицу движений, индексы журнала и связи с остатком, обслуживанием и справочником.
    /// </summary>
    public void Configure(EntityTypeBuilder<OrganizationMaterialMovement> builder)
    {
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.TypeCode)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.Quantity)
            .HasPrecision(18, 3);

        builder.Property(x => x.EnteredQuantity)
            .HasPrecision(18, 3);

        builder.Property(x => x.EnteredUnit)
            .HasMaxLength(32);

        builder.Property(x => x.ConversionFactor)
            .HasPrecision(18, 6);

        builder.Property(x => x.Unit)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.UnitCost)
            .HasPrecision(18, 8);

        builder.Property(x => x.TotalCost)
            .HasPrecision(18, 2);

        builder.Property(x => x.QuantityBalanceAfter)
            .HasPrecision(18, 3);

        builder.Property(x => x.CostBalanceAfter)
            .HasPrecision(18, 2);

        builder.Property(x => x.SourceEntityType)
            .HasMaxLength(80);

        builder.Property(x => x.Comment)
            .HasMaxLength(1000);

        builder.Property(x => x.CreatedByUserId)
            .HasMaxLength(450);

        builder.HasIndex(x => new { x.OrganizationId, x.CreatedAt });
        builder.HasIndex(x => new { x.OrganizationId, x.StockId, x.CreatedAt });
        builder.HasIndex(x => new { x.OrganizationId, x.SourceEntityType, x.SourceEntityId });
        builder.HasIndex(x => new { x.MaintenanceId, x.TypeCode });
        builder.HasIndex(x => x.OperationId);
        builder.HasIndex(x => x.CommandId).IsUnique();

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Stock)
            .WithMany()
            .HasForeignKey(x => x.StockId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.CatalogItem)
            .WithMany()
            .HasForeignKey(x => x.CatalogItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Maintenance)
            .WithMany()
            .HasForeignKey(x => x.MaintenanceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
