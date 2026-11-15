namespace OpenCRM.Admin.Api.Models;

/// <summary> Запись аудита организации в административной панели. </summary>
public sealed class AdminOrganizationAudit
{
    public string EventType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime CreatedAt { get; init; }
}
