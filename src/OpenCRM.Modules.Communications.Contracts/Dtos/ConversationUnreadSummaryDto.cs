namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Счётчик непрочитанных сообщений и версия активности для повторной синхронизации. </summary>
public sealed record ConversationUnreadSummaryDto
{
    public int UnreadCount { get; init; }
    public string Revision { get; init; } = string.Empty;
}
