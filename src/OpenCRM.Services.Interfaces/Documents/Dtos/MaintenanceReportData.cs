using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Services.Interfaces.Documents.Dtos;

/// <summary>Данные обслуживания и связанной техники для обычных отчётов.</summary>
public sealed record MaintenanceReportData(
    GetMaintenanceByIdDto Maintenance,
    GetVehicleDto? Vehicle);
