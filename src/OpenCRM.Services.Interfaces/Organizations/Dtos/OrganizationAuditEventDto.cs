namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Событие аудита организации. </summary>
public sealed class OrganizationAuditEventDto
{
    /// <summary> Идентификатор события. </summary>
    public Guid Id { get; init; }

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; init; }

    /// <summary> Идентификатор пользователя, совершившего действие. </summary>
    public string ActorUserId { get; init; } = string.Empty;

    /// <summary> Отображаемое имя автора события. </summary>
    public string ActorDisplayName { get; init; } = string.Empty;

    /// <summary> Тип события. </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary> Заголовок события. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Описание события. </summary>
    public string? Description { get; init; }

    /// <summary> Тип объекта события. </summary>
    public string? TargetType { get; init; }

    /// <summary> Идентификатор объекта события. </summary>
    public string? TargetId { get; init; }

    /// <summary> Дата и время создания события. </summary>
    public DateTime CreatedAt { get; init; }
}
