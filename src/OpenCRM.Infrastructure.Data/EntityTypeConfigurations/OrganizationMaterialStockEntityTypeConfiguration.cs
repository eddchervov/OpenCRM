using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCRM.Domain;

namespace OpenCRM.Infrastructure.Data.EntityTypeConfigurations;

/// <summary>
/// EF-конфигурация складского остатка материала организации.
/// </summary>
public class OrganizationMaterialStockEntityTypeConfiguration : IEntityTypeConfiguration<OrganizationMaterialStock>
{
    /// <summary>
    /// Настраивает таблицу остатков, индексы уникальности и связи с организацией/scenario-catalog.
    /// </summary>
    public void Configure(EntityTypeBuilder<OrganizationMaterialStock> builder)
    {
        builder.Property(stock => stock.CurrencyCode).HasMaxLength(3).HasDefaultValue("RUB").IsRequired();
        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Name)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(x => x.Unit)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.QuantityOnHand)
            .HasPrecision(18, 3);

        builder.Property(x => x.ReservedQuantity)
            .HasPrecision(18, 3);

        builder.Property(x => x.MinQuantity)
            .HasPrecision(18, 3);

        builder.Property(x => x.AverageUnitCost)
            .HasPrecision(18, 8);

        builder.Property(x => x.LegacyUnit)
            .HasMaxLength(32);

        builder.Property(x => x.PackageQuantity)
            .HasPrecision(18, 3);

        builder.Property(x => x.InventoryValue)
            .HasPrecision(18, 2);

        builder.HasIndex(x => new { x.OrganizationId, x.CatalogItemId })
            .IsUnique()
            .HasFilter("\"CatalogItemId\" IS NOT NULL");

        builder.HasIndex(x => new { x.OrganizationId, x.Name, x.Unit })
            .IsUnique();

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.CatalogItem)
            .WithMany()
            .HasForeignKey(x => x.CatalogItemId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
