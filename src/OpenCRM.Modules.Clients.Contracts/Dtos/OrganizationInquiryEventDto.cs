namespace OpenCRM.Modules.Clients.Contracts.Dtos;

/// <summary> Событие истории ручного обращения. </summary>
public sealed class OrganizationInquiryEventDto
{
    public Guid Id { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? PreviousValue { get; init; }
    public string? NewValue { get; init; }
    public string ActorUserId { get; init; } = string.Empty;
    public string? ActorName { get; init; }
    public DateTime CreatedAt { get; init; }
}
