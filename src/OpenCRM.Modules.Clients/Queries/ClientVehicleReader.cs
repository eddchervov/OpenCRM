using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Modules.Clients.Rules;
using OpenCRM.Modules.Vehicles.Contracts;

namespace OpenCRM.Modules.Clients.Queries;

/// <summary> Читатель техники клиентов организации. </summary>
internal sealed class ClientVehicleReader(IVehicleClientSummaryQueryService vehicles)
{
    /// <summary> Зависимость сервиса сводок техники клиентов. </summary>
    private readonly IVehicleClientSummaryQueryService _vehicles = vehicles;

    /// <summary> Загружает технику по каждому клиенту. </summary>
    internal async Task<IReadOnlyDictionary<Guid, IReadOnlyList<OrganizationClientVehicleDto>>> LoadAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> clientIds,
        bool sortByName)
    {
        var rows = await this._vehicles.GetByClientIdsAsync(organizationId, clientIds, sortByName);

        return rows.GroupBy(vehicle => vehicle.ClientId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<OrganizationClientVehicleDto>)group.Select(vehicle => new OrganizationClientVehicleDto
                {
                    Id = vehicle.Id,
                    DisplayName = ClientRules.BuildVehicleDisplayName(
                        vehicle.BrandName, vehicle.ModelName, vehicle.YearIssue, vehicle.Name, vehicle.RegistrationPlate),
                    BrandLogoKey = vehicle.BrandLogoKey,
                    RegistrationPlate = vehicle.RegistrationPlate,
                    MaintenanceCount = vehicle.MaintenanceCount,
                    LastMaintenanceDate = vehicle.LastMaintenanceDate
                }).ToList());
    }
}
