namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Диалог для общего списка переписок. </summary>
public sealed record ConversationSummaryDto
{
    public Guid Id { get; init; }
    public Guid? ClientId { get; init; }
    public string? ClientName { get; init; }
    public long? BotId { get; init; }
    public Guid? ConnectionId { get; init; }
    public string ChannelCode { get; init; } = string.Empty;
    public string BotUsername { get; init; } = string.Empty;
    public string? TelegramDisplayName { get; init; }
    public string? ExternalDisplayName { get; init; }
    public string? LastMessageText { get; init; }
    public DateTime? LastMessageAt { get; init; }
    public ConversationMessageDirection? LastMessageDirection { get; init; }
    public int UnreadCount { get; init; }
    public bool IsReadOnly { get; init; }
}
