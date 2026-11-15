namespace OpenCRM.Services.Interfaces.Defects.Dtos;

/// <summary> Вариант дефекта для привязки к обслуживанию. </summary>
public class VehicleDefectOptionDto
{
    /// <summary> Идентификатор дефекта. </summary>
    public Guid Id { get; set; }

    /// <summary> Название дефекта. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Описание дефекта, если указано. </summary>
    public string? Description { get; set; }

    /// <summary> Код критичности дефекта. </summary>
    public string SeverityCode { get; set; } = string.Empty;

    /// <summary> Код статуса дефекта. </summary>
    public string StatusCode { get; set; } = string.Empty;

    /// <summary> Признак уже существующей связи с обслуживанием. </summary>
    public bool IsLinked { get; set; }
}
