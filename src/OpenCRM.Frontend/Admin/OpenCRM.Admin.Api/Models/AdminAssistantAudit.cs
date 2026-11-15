namespace OpenCRM.Admin.Api.Models;

/// <summary> Запись журнала изменений настроек помощника. </summary>
public sealed class AdminAssistantAudit
{
    public Guid Id { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public string AdminUserId { get; init; } = string.Empty;
}
