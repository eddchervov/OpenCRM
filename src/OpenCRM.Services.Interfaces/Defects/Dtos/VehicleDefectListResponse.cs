using OpenCRM.Domain.Pagination;

namespace OpenCRM.Services.Interfaces.Defects.Dtos;

/// <summary> Страница списка дефектов техники. </summary>
public class VehicleDefectListResponse : PagedResult<VehicleDefectDto>
{
    /// <summary> Количество открытых дефектов. </summary>
    public required int OpenCount { get; set; }

    /// <summary> Количество решённых дефектов. </summary>
    public required int ResolvedCount { get; set; }

    /// <summary> Признак наличия следующих страниц. </summary>
    public required bool HasMore { get; set; }

    /// <summary> Идентификатор выбранной техники, если есть. </summary>
    public required Guid? SelectedVehicleId { get; set; }
}
