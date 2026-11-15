namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Событие истории клиента организации. </summary>
public class OrganizationClientHistoryEventDto
{
    /// <summary> Идентификатор события. </summary>
    public Guid Id { get; set; }

    /// <summary> Идентификатор клиента организации. </summary>
    public Guid OrganizationClientId { get; set; }

    /// <summary> Тип события. </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary> Заголовок события. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Описание события. </summary>
    public string? Description { get; set; }

    /// <summary> Отображаемое имя автора события. </summary>
    public string? ActorDisplayName { get; set; }

    /// <summary> Тип связанной сущности. </summary>
    public string? RelatedEntityType { get; set; }

    /// <summary> Идентификатор связанной сущности. </summary>
    public Guid? RelatedEntityId { get; set; }

    /// <summary> Дата создания события. </summary>
    public DateTime CreatedAt { get; set; }
}
