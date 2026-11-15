namespace OpenCRM.Modules.Inventory.Contracts;

/// <summary> Результат расчёта списания материалов по обслуживанию с итоговой стоимостью. </summary>
public sealed record MaintenanceConsumptionResult(
    decimal TotalCost,
    IReadOnlyList<MaintenanceMaterialConsumptionCost> Details);
