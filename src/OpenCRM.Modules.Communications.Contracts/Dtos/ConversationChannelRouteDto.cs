namespace OpenCRM.Modules.Communications.Contracts.Dtos;

public sealed record ConversationChannelRouteDto
{
    public Guid ConversationId { get; init; }
    public Guid ConnectionId { get; init; }
    public string ChannelCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsReadOnly { get; init; }
    public long UnreadCount { get; init; }
    public DateTime? LastMessageAt { get; init; }
}
