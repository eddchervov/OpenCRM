namespace OpenCRM.Modules.Communications.Contracts.Dtos;

public sealed record ConversationClientRoutesDto
{
    public Guid? ClientId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public bool IsUnknownContact { get; init; }
    public IReadOnlyList<ConversationChannelRouteDto> ActiveRoutes { get; init; } = [];
    public IReadOnlyList<ConversationChannelRouteDto> HistoryRoutes { get; init; } = [];
}
