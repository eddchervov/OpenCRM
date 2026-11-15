using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles;

/// <summary> Поиск техники для модуля обслуживания. </summary>
public sealed class VehicleMaintenanceLookupService(IVehicleRepository vehicles) : IVehicleMaintenanceLookupService
{
    /// <summary> Возвращает данные техники для обслуживания по идентификатору. </summary>
    public Task<MaintenanceVehicleDto?> FindAsync(string userId, Guid? organizationId, Guid vehicleId) =>
        vehicles.Get(userId, organizationId, vehicle => new MaintenanceVehicleDto
            {
                Id = vehicle.Id,
                OrganizationClientId = vehicle.OrganizationClientId
            })
            .AsNoTracking()
            .Where(vehicle => vehicle.Id == vehicleId)
            .FirstOrDefaultAsync();
}
