using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Inventory.Materials;

namespace OpenCRM.Modules.Inventory.Commands;

/// <summary> Работает со складскими остатками материалов организации. </summary>
internal sealed class InventoryStockRepository(OpenCRMContext context, ICatalogMaterialLookup catalogMaterialLookup)
{
    /// <summary> Определяет материал склада по каталогу или наименованию. </summary>
    internal Task<(Guid? CatalogItemId, string Name, string Unit)> ResolveMaterialAsync(
        Guid organizationId,
        Guid? catalogItemId,
        string? name,
        string? unit) => this.ResolveMaterialAsync(organizationId, catalogItemId, name, unit, false);

    /// <summary> Определяет материал при приёмке с учётом активности каталога. </summary>
    internal Task<(Guid? CatalogItemId, string Name, string Unit)> ResolveReceiptMaterialAsync(
        Guid organizationId,
        Guid? catalogItemId,
        string? name,
        string? unit) => this.ResolveMaterialAsync(organizationId, catalogItemId, name, unit, true);

    /// <summary> Определяет материал с заданными требованиями к записи каталога. </summary>
    private async Task<(Guid? CatalogItemId, string Name, string Unit)> ResolveMaterialAsync(
        Guid organizationId,
        Guid? catalogItemId,
        string? name,
        string? unit,
        bool requireActive)
    {
        if (catalogItemId.HasValue)
        {
            var catalogItem = await catalogMaterialLookup.GetMaterialAsync(organizationId, catalogItemId.Value, requireActive)
                ?? throw new InvalidOperationException("Материал не найден в справочнике сценария.");

            return (catalogItem.Id, catalogItem.Name, InventoryUnitRules.NormalizeUnit(unit));
        }

        var materialName = name?.Trim();
        if (string.IsNullOrWhiteSpace(materialName))
        {
            throw new InvalidOperationException("Укажите материал.");
        }

        return (null, materialName, InventoryUnitRules.NormalizeUnit(unit));
    }

    /// <summary> Находит складской остаток материала или создаёт новый. </summary>
    internal async Task<OrganizationMaterialStock> GetOrCreateAsync(
        Guid organizationId,
        Guid? catalogItemId,
        string name,
        string unit)
    {
        var stock = await this.FindAsync(organizationId, catalogItemId, name, unit);
        if (stock != null)
        {
            return stock;
        }

        // Блокировка организации защищает создание ещё отсутствующей складской позиции.
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Organizations" WHERE "Id" = {organizationId} FOR NO KEY UPDATE""");
        stock = await this.FindAsync(organizationId, catalogItemId, name, unit);
        if (stock != null)
        {
            return stock;
        }

        stock = new OrganizationMaterialStock
        {
            OrganizationId = organizationId,
            CurrencyCode = await context.Organizations.Where(organization => organization.Id == organizationId)
                .Select(organization => organization.CurrencyCode).SingleAsync(),
            CatalogItemId = catalogItemId,
            Name = name,
            Unit = unit,
            QuantityOnHand = 0,
            ReservedQuantity = 0,
            MinQuantity = 0,
            AverageUnitCost = 0,
            InventoryValue = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.OrganizationMaterialStocks.Add(stock);

        return stock;
    }

    /// <summary> Ищет складской остаток по организации и параметрам материала. </summary>
    private Task<OrganizationMaterialStock?> FindAsync(Guid organizationId, Guid? catalogItemId, string name, string unit)
    {
        if (catalogItemId.HasValue)
        {
            return context.OrganizationMaterialStocks
                .FirstOrDefaultAsync(item => item.OrganizationId == organizationId && item.CatalogItemId == catalogItemId.Value);
        }

        var legacyUnit = unit switch
        {
            "г" => "кг",
            "мл" => "л",
            _ => unit
        };

        return context.OrganizationMaterialStocks
            .Where(item => item.OrganizationId == organizationId && item.Name == name
                && (item.Unit == unit || item.Unit == legacyUnit))
            .OrderByDescending(item => item.Unit == unit)
            .FirstOrDefaultAsync();
    }
}
