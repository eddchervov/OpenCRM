namespace OpenCRM.Modules.Vehicles.Contracts.Dtos;

/// <summary> Данные для сохранения показания счетчика техники. </summary>
public class UpdateVehicleCounterDto
{
    /// <summary> Идентификатор существующей записи, если обновляется. </summary>
    public Guid? EntryId { get; set; }

    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Значение показания счетчика. </summary>
    public int Value { get; set; }

    /// <summary> Время фиксации показания. </summary>
    public DateTime RecordedAt { get; set; }

    /// <summary> Комментарий к показанию. </summary>
    public string? Comment { get; set; }
}
