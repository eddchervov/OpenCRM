namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Данные для создания события аудита организации. </summary>
public sealed class OrganizationAuditEventCreateDto
{
    /// <summary>
    /// Устойчивый идентификатор события для проверки фиксации повторяемой команды.
    /// </summary>
    public Guid? EventId { get; init; }

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; init; }

    /// <summary> Идентификатор пользователя, совершившего действие. </summary>
    public string ActorUserId { get; init; } = string.Empty;

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
}
