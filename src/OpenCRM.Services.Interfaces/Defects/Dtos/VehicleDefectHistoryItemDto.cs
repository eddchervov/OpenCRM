namespace OpenCRM.Services.Interfaces.Defects.Dtos;

/// <summary> Запись истории изменений дефекта. </summary>
public class VehicleDefectHistoryItemDto
{
    /// <summary> Время создания записи. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Название дефекта на момент записи. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Описание дефекта на момент записи. </summary>
    public string? Description { get; set; }

    /// <summary> Имя пользователя, изменившего дефект, если известно. </summary>
    public string? ActorDisplayName { get; set; }
}
