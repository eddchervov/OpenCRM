namespace OpenCRM.Services.Interfaces.Defects.Dtos;

/// <summary> Данные для создания дефекта техники. </summary>
public class CreateVehicleDefectDto
{
    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Название дефекта. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Описание дефекта, если указано. </summary>
    public string? Description { get; set; }

    /// <summary> Код критичности дефекта. </summary>
    public string SeverityCode { get; set; } = string.Empty;

    /// <summary> Код статуса дефекта. </summary>
    public string StatusCode { get; set; } = string.Empty;
}
