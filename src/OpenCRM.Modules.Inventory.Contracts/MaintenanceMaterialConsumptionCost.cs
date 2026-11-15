namespace OpenCRM.Modules.Inventory.Contracts;

/// <summary> Стоимость списанного материала по строке работы. </summary>
public sealed record MaintenanceMaterialConsumptionCost(
    Guid DetailId,
    decimal? UnitCost,
    decimal? TotalCost);
