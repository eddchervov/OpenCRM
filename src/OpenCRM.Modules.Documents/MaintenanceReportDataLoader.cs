using OpenCRM.Services.Interfaces.Documents.Dtos;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Vehicles.Contracts;

namespace OpenCRM.Modules.Documents;

/// <summary>Загружает обслуживание, затем связанную с ним технику.</summary>
public sealed class MaintenanceReportDataLoader(
    IMaintenanceQueryService maintenanceQueryService,
    IVehicleQueryService vehicleService) : IMaintenanceReportDataLoader
{
    /// <summary> Загружает данные для отчёта по обслуживанию. </summary>
    public async Task<MaintenanceReportData?> LoadAsync(string userId, Guid? organizationId, Guid maintenanceId)
    {
        var maintenance = await maintenanceQueryService.GetById(userId, organizationId, maintenanceId);
        if (maintenance == null)
        {
            return null;
        }

        var vehicle = await vehicleService.GetById(userId, organizationId, maintenance.VehicleId);

        return new MaintenanceReportData(maintenance, vehicle);
    }
}
