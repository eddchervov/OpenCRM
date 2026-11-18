using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Снимок состояния обслуживания для отслеживания изменений. </summary>
internal sealed record MaintenanceSnapshot(
    string CurrencyCode,
    MaintenanceStatus Status,
    MaintenanceType Type,
    DateTime? PlannedDate,
    DateTime? PlannedEndDate,
    TimeSpan? PlannedStartTime,
    TimeSpan? PlannedEndTime,
    DateTime? CompletedDate,
    DateTime? ReadyForHandoverAt,
    DateTime? HandedOverAt,
    string? Location,
    Guid? EmployeeId,
    string? Performer,
    int? Mileage,
    string? Comment,
    decimal DiscountAmount,
    decimal PaidAmount,
    decimal LaborCostAmount,
    IReadOnlyList<MaintenanceDetailSnapshot> Details)
{
    /// <summary> Создаёт снимок по записи обслуживания. </summary>
    public static MaintenanceSnapshot From(MaintenanceEntity maintenance)
    {
        return new MaintenanceSnapshot(
            maintenance.CurrencyCode,
            maintenance.Status,
            maintenance.Type,
            MaintenanceDateRules.NormalizeMaintenanceDate(maintenance.PlannedDate),
            MaintenanceDateRules.NormalizeMaintenanceDate(maintenance.PlannedEndDate),
            maintenance.PlannedStartTime,
            maintenance.PlannedEndTime,
            MaintenanceDateRules.NormalizeMaintenanceDate(maintenance.CompletedDate),
            maintenance.ReadyForHandoverAt,
            maintenance.HandedOverAt,
            maintenance.Location,
            maintenance.EmployeeId,
            maintenance.Performer,
            maintenance.Mileage,
            maintenance.Comment,
            maintenance.DiscountAmount,
            maintenance.PaidAmount,
            maintenance.LaborCostAmount,
            maintenance.Details
                .Select(x => new MaintenanceDetailSnapshot(
                    x.DetailId,
                    x.EmployeeId,
                    x.EmployeeName,
                    x.CatalogItemId,
                    x.Name,
                    x.Description,
                    x.Price,
                    x.VatRateCode,
                    x.Quantity,
                    x.MaterialCatalogItemId,
                    x.MaterialUnit,
                    x.MaterialTotalCost,
                    x.MaterialName,
                    x.MaterialQuantity,
                    x.MaterialUnitCost))
                .ToList());
    }
}
