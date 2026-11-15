namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Страница переписок с курсором по клиентским группам. </summary>
public sealed record ConversationClientInboxPageDto
{
    public IReadOnlyList<ConversationClientInboxGroupDto> Items { get; init; } = [];
    public string? NextCursor { get; init; }
    public int UnreadCount { get; init; }
}
