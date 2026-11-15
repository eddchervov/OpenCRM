namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Группа каналов клиента или отдельный диалог неизвестного контакта. </summary>
public sealed record ConversationClientInboxGroupDto
{
    public Guid GroupId { get; init; }
    public Guid? ClientId { get; init; }
    public string? ClientName { get; init; }
    public string? ExternalDisplayName { get; init; }
    public string? LastMessageText { get; init; }
    public DateTime? LastMessageAt { get; init; }
    public int UnreadCount { get; init; }
    public IReadOnlyList<ConversationSummaryDto> Routes { get; init; } = [];
}
