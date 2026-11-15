using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Contracts;

/// <summary> Читает технику и показатели обслуживания клиентов организации. </summary>
public interface IVehicleClientSummaryQueryService
{
    /// <summary>
    /// Возвращает технику после проверки вызывающим сценарием возможности ClientsModule
    /// и права ClientsView, без повторной проверки доступа.
    /// </summary>
    Task<IReadOnlyList<VehicleClientSummaryDto>> GetByClientIdsAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> clientIds,
        bool sortByName);
}
