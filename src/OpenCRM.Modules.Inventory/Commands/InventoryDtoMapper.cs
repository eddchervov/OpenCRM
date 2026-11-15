using OpenCRM.Domain;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Modules.Inventory.Materials;

namespace OpenCRM.Modules.Inventory.Commands;

/// <summary> Преобразует сущности инвентаризации в DTO. </summary>
internal static class InventoryDtoMapper
{
    /// <summary> Преобразует остаток материала в DTO. </summary>
    internal static OrganizationMaterialStockDto MapStock(OrganizationMaterialStock stock) => new()
    {
        Id = stock.Id,
        CatalogItemId = stock.CatalogItemId,
        Name = stock.Name,
        Unit = InventoryUnitRules.NormalizeStoredUnit(stock.Unit),
        PackageQuantity = stock.PackageQuantity,
        QuantityOnHand = stock.QuantityOnHand,
        ReservedQuantity = stock.ReservedQuantity,
        AvailableQuantity = stock.QuantityOnHand - stock.ReservedQuantity,
        MinQuantity = stock.MinQuantity,
        AverageUnitCost = stock.AverageUnitCost,
        InventoryValue = stock.InventoryValue,
        UpdatedAt = stock.UpdatedAt,
        IsLowStock = stock.QuantityOnHand <= stock.MinQuantity,
        IsOutOfStock = stock.QuantityOnHand <= 0
    };

    /// <summary> Преобразует движение материала в DTO выдачи для операции. </summary>
    internal static OperationMaterialIssueDto MapOperationIssue(OrganizationMaterialMovement movement, OrganizationMaterialStock stock) => new()
    {
        Id = movement.Id,
        StockId = stock.Id,
        MaterialName = stock.Name,
        Quantity = Math.Abs(movement.Quantity),
        Unit = InventoryUnitRules.NormalizeStoredUnit(stock.Unit),
        CreatedAt = movement.CreatedAt,
        IsReturn = movement.Quantity > 0,
        SourceMovementId = movement.SourceEntityType == "operation_return" ? movement.SourceEntityId : null
    };

    /// <summary> Приводит комментарий к нормализованному виду. </summary>
    internal static string? NormalizeComment(string? value)
    {
        var comment = value?.Trim();
        return string.IsNullOrWhiteSpace(comment) ? null : comment.Length > 1000 ? comment[..1000] : comment;
    }

    /// <summary> Форматирует количество материала для отображения. </summary>
    internal static string FormatQuantity(decimal quantity) => quantity.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
}
