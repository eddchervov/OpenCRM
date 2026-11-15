using OpenCRM.Domain.Pagination;

namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Страница истории показаний счетчиков. </summary>
public class VehicleCounterHistoryResponse : PagedResult<VehicleCounterEntryDto>
{
    /// <summary> Признак наличия ещё записей. </summary>
    public required bool HasMore { get; set; }
}
