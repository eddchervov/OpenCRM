namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Событие истории обслуживания. </summary>
public class MaintenanceHistoryEventDto
{
    /// <summary> Идентификатор события. </summary>
    public Guid Id { get; set; }

    /// <summary> Тип события. </summary>
    public string EventType { get; set; } = default!;

    /// <summary> Идентификатор обращения, из которого создано обслуживание. </summary>
    public Guid? SourceInquiryId { get; set; }

    /// <summary> Часовой пояс организации, если событие принадлежит рабочему пространству. </summary>
    public string? TimeZoneId { get; set; }

    /// <summary> Заголовок события. </summary>
    public string Title { get; set; } = default!;

    /// <summary> Подробности события. </summary>
    public string? Description { get; set; }

    /// <summary> Отображаемое имя пользователя на момент события. </summary>
    public string? ActorDisplayName { get; set; }

    /// <summary> Дата создания события. </summary>
    public DateTime CreatedAt { get; set; }
}
