namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Страница общего списка диалогов. </summary>
public sealed record ConversationInboxPageDto
{
    public IReadOnlyList<ConversationSummaryDto> Items { get; init; } = [];
    public string? NextCursor { get; init; }
    public int UnreadCount { get; init; }
}
