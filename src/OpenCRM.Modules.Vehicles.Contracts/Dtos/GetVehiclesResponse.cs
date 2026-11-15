using OpenCRM.Domain.Pagination;

namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Страница списка техники с фильтрами пользователя. </summary>
public class GetVehiclesResponse : PagedResult<GetVehicleDto>
{
    /// <summary> Признак доступности сведений об обслуживании в списке техники. </summary>
    public bool CanViewMaintenance { get; set; }

    /// <summary> Варианты брендов техники пользователя для фильтра. </summary>
    public required IEnumerable<VehicleBrandFilterOptionDto> UserBrands { get; set; }

    /// <summary> Виды техники пользователя для фильтра. </summary>
    public required IEnumerable<int> UserKinds { get; set; }
}
