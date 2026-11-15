using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Запись события истории клиента организации. </summary>
public class OrganizationClientHistoryEvent
{
    /// <summary> Идентификатор события. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Идентификатор клиента организации. </summary>
    public Guid OrganizationClientId { get; set; }

    /// <summary> Клиент организации, к которому относится событие. </summary>
    public OrganizationClient OrganizationClient { get; set; } = null!;

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Идентификатор пользователя, инициировавшего событие. </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary> Пользователь, инициировавший событие. </summary>
    public ApplicationUser User { get; set; } = null!;

    /// <summary> Отображаемое имя автора события. </summary>
    public string? ActorDisplayName { get; set; }

    /// <summary> Код типа события истории. </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary> Заголовок события. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Описание события. </summary>
    public string? Description { get; set; }

    /// <summary> Тип связанной сущности события. </summary>
    public string? RelatedEntityType { get; set; }

    /// <summary> Идентификатор связанной сущности события. </summary>
    public Guid? RelatedEntityId { get; set; }

    /// <summary> Дата и время создания события. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
