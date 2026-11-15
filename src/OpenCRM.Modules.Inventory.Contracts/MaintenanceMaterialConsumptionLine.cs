namespace OpenCRM.Modules.Inventory.Contracts;

/// <summary> Данные о строке расхода материала по работе обслуживания. </summary>
public sealed record MaintenanceMaterialConsumptionLine(
    Guid DetailId,
    Guid? CatalogItemId,
    string? Name,
    string? MaterialName,
    string? Unit,
    decimal? Quantity);
