using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.Modules.Clients.Contracts;

/// <summary> Контракт связей клиентов с транспортными средствами. </summary>
public interface IOrganizationClientVehicleLinkService
{
    /// <summary> Отвязывает транспортное средство от клиента. </summary>
    Task UnlinkVehicleAsync(string userId, Guid organizationId, Guid clientId, Guid vehicleId);

    /// <summary> Передаёт транспортное средство другому клиенту. </summary>
    Task TransferVehicleAsync(string userId, Guid organizationId, Guid clientId, Guid vehicleId, Guid targetClientId);
}
