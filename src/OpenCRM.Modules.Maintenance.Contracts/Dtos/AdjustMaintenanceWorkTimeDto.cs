namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные для корректировки рабочего времени по работе. </summary>
public sealed class AdjustMaintenanceWorkTimeDto
{
    /// <summary> Идентификатор команды корректировки. </summary>
    public Guid CommandId { get; set; }

    /// <summary> Величина корректировки в минутах. </summary>
    public int Minutes { get; set; }

    /// <summary> Причина корректировки времени. </summary>
    public string Reason { get; set; } = string.Empty;
}
