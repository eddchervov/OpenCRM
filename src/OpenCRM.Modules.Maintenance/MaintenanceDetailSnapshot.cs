
namespace OpenCRM.Modules.Maintenance;

/// <summary> Снимок строки работы или услуги обслуживания с данными материала. </summary>
internal sealed record MaintenanceDetailSnapshot(
    Guid DetailId,
    Guid? EmployeeId,
    string? EmployeeName,
    Guid? CatalogItemId,
    string? Name,
    string? Description,
    decimal? Price,
    string? VatRateCode,
    int Quantity,
    Guid? MaterialCatalogItemId,
    string? MaterialUnit,
    decimal? MaterialTotalCost,
    string? MaterialName,
    decimal? MaterialQuantity,
    decimal? MaterialUnitCost);
