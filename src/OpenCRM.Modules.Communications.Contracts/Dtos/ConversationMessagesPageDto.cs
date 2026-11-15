namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Страница сообщений одного диалога в обратном хронологическом порядке. </summary>
public sealed record ConversationMessagesPageDto
{
    public Guid ConversationId { get; init; }
    public ConversationSummaryDto Conversation { get; init; } = new();
    public IReadOnlyList<ConversationMessageDto> Items { get; init; } = [];
    public long? NextBeforeSequence { get; init; }
    public long LastSequence { get; init; }
    public bool IsReadOnly { get; init; }
    public bool CanSend { get; init; }
    public bool CanManage { get; init; }
    public bool CanViewClient { get; init; }
    public bool CanViewMaintenance { get; init; }
    public string? TimeZoneId { get; init; }
}
