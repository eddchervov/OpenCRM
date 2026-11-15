using OpenCRM.Domain.Pagination;

namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Страница техники со счетчиками. </summary>
public class VehicleCounterVehiclesResponse : PagedResult<VehicleCounterVehicleDto>
{
    /// <summary> Количество техники со счетчиками. </summary>
    public required int VehiclesWithCountersCount { get; set; }

    /// <summary> Признак наличия ещё записей. </summary>
    public required bool HasMore { get; set; }
}
