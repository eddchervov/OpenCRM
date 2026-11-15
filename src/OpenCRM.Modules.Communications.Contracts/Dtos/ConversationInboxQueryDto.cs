namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Параметры поиска диалогов организации. </summary>
public sealed record ConversationInboxQueryDto
{
    public string? Search { get; init; }
    public Guid? ClientId { get; init; }
    public bool OnlyUnread { get; init; }
    public string? Cursor { get; init; }
    public int PageSize { get; init; } = 30;
}
