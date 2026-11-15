using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Contracts;

/// <summary> Читает доступную технику для сценариев обслуживания без загрузки карточки. </summary>
/// <remarks> Права обслуживания проверяет вызывающий сценарий; архивная техника допускается, удалённая исключается. </remarks>
public interface IVehicleMaintenanceLookupService
{
    /// <summary> Находит доступную для обслуживания технику. </summary>
    Task<MaintenanceVehicleDto?> FindAsync(string userId, Guid? organizationId, Guid vehicleId);
}
