using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Services.Interfaces.Documents.Dtos;

/// <summary>Данные отчёта техники после проверки области существующими сервисами чтения.</summary>
public sealed record VehicleDocumentData(
    GetVehicleDto? Vehicle,
    IReadOnlyList<GetMaintenanceDto> MaintenanceHistory);
