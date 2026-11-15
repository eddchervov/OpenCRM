using OpenCRM.Modules.Inventory.Contracts;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Синхронизирует расход материалов по обслуживанию. </summary>
internal sealed class MaintenanceConsumption(
    IMaintenanceConsumptionService maintenanceConsumptionService)
{
    /// <summary> Синхронизирует расход материалов записи обслуживания. </summary>
    internal async Task SyncMaterialConsumptionAsync(string userId, Guid organizationId, MaintenanceEntity maintenance)
    {
        var details = maintenance.Details.Select(detail => new MaintenanceMaterialConsumptionLine(
            detail.DetailId,
            detail.MaterialCatalogItemId,
            detail.Name,
            detail.MaterialName,
            detail.MaterialUnit,
            detail.MaterialQuantity)).ToArray();
        var result = await maintenanceConsumptionService.SynchronizeAsync(userId, organizationId, maintenance.Id, details);
        var costsByDetailId = result.Details.ToDictionary(detail => detail.DetailId);
        foreach (var detail in maintenance.Details)
        {
            if (costsByDetailId.TryGetValue(detail.DetailId, out var cost))
            {
                detail.MaterialUnitCost = cost.UnitCost;
                detail.MaterialTotalCost = cost.TotalCost;
                continue;
            }

            detail.MaterialUnitCost = 0;
            detail.MaterialTotalCost = 0;
        }

        maintenance.MaterialCostAmount = result.TotalCost;
    }
}
