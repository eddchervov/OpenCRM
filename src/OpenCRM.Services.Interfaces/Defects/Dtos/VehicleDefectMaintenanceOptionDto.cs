namespace OpenCRM.Services.Interfaces.Defects.Dtos;

/// <summary> Вариант обслуживания для связи с дефектом. </summary>
public class VehicleDefectMaintenanceOptionDto
{
    /// <summary> Идентификатор обслуживания. </summary>
    public Guid Id { get; set; }

    /// <summary> Текст варианта обслуживания. </summary>
    public string Text { get; set; } = string.Empty;
}
