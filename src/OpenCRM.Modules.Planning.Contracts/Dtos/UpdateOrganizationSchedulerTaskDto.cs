namespace OpenCRM.Modules.Planning.Contracts.Dtos;

/// <summary> Данные для обновления задачи планировщика организации. </summary>
public sealed class UpdateOrganizationSchedulerTaskDto
{
    /// <summary> Заголовок задачи. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Комментарий к задаче. </summary>
    public string? Comment { get; set; }

    /// <summary> Идентификатор связанного транспортного средства, если есть. </summary>
    public Guid? VehicleId { get; set; }

    /// <summary> Идентификатор клиента организации, если есть. </summary>
    public Guid? OrganizationClientId { get; set; }

    /// <summary> Идентификатор ответственного пользователя, если есть. </summary>
    public string? ResponsibleUserId { get; set; }

    /// <summary> Код типа задачи, если задан. </summary>
    public string? TypeCode { get; set; }

    /// <summary> Код приоритета задачи, если задан. </summary>
    public string? PriorityCode { get; set; }

    /// <summary> Время начала задачи, если задано. </summary>
    public DateTime? StartAt { get; set; }

    /// <summary> Время окончания задачи, если задано. </summary>
    public DateTime? EndAt { get; set; }

    /// <summary> Идентификатор связанного дефекта, если есть. </summary>
    public Guid? LinkedDefectId { get; set; }

    /// <summary> Идентификатор связанного обслуживания, если есть. </summary>
    public Guid? LinkedMaintenanceId { get; set; }

    /// <summary> Код правила повторения, если задан. </summary>
    public string? RecurrenceCode { get; set; }

    /// <summary> Интервал повторения, если задан. </summary>
    public int? RecurrenceInterval { get; set; }

    /// <summary> Дата окончания повторения, если задана. </summary>
    public DateTime? RecurrenceUntil { get; set; }

    /// <summary> Время напоминания, если задано. </summary>
    public DateTime? ReminderAt { get; set; }
}
