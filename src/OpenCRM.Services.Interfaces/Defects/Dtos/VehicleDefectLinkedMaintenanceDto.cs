namespace OpenCRM.Services.Interfaces.Defects.Dtos;

/// <summary> Обслуживание, связанное с дефектом. </summary>
public class VehicleDefectLinkedMaintenanceDto
{
    /// <summary> Идентификатор обслуживания. </summary>
    public Guid Id { get; set; }

    /// <summary> Дата обслуживания. </summary>
    public DateTime Date { get; set; }

    /// <summary> Плановая дата, если указана. </summary>
    public DateTime? PlannedDate { get; set; }

    /// <summary> Дата выполнения, если выполнено. </summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary> Код статуса обслуживания. </summary>
    public int Status { get; set; }

    /// <summary> Название типа обслуживания. </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary> Комментарий, если есть. </summary>
    public string? Comment { get; set; }

    /// <summary> Пробег, если указан. </summary>
    public int? Mileage { get; set; }

    /// <summary> Стоимость. </summary>
    public decimal Price { get; set; }
}
