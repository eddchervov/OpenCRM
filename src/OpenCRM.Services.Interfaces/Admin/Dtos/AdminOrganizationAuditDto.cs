namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO записи аудита организации. </summary>
public sealed class AdminOrganizationAuditDto
{
    /// <summary> Тип события. </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary> Заголовок записи. </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary> Описание записи. </summary>
    public string? Description { get; init; }

    /// <summary> Время создания записи. </summary>
    public DateTime CreatedAt { get; init; }
}

