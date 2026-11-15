using OpenCRM.Services.Interfaces.Documents.Dtos;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Vehicles.Contracts;

namespace OpenCRM.Modules.Documents;

/// <summary>Общая загрузка данных отчёта техники для PDF и Word.</summary>
public sealed class VehicleDocumentDataLoader(
    IVehicleQueryService vehicleService,
    IMaintenanceQueryService maintenanceQueryService) : IVehicleDocumentDataLoader
{
    /// <summary> Загружает данные техники и историю обслуживания для отчёта. </summary>
    public async Task<VehicleDocumentData> LoadAsync(string userId, Guid? organizationId, Guid vehicleId)
    {
        var vehicle = await vehicleService.GetById(userId, organizationId, vehicleId);
        if (vehicle == null)
        {
            return new VehicleDocumentData(null, []);
        }

        var history = await maintenanceQueryService.Get(userId, organizationId, vehicle.Id);

        return new VehicleDocumentData(vehicle, history.ToList());
    }
}
