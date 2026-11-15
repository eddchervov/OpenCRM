namespace OpenCRM.Modules.Communications.Contracts.Dtos;

public sealed record MessageTemplateGroupDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public int Version { get; init; }
    public bool IsArchived { get; init; }
    public IReadOnlyList<MessageTemplateDto> Templates { get; init; } = [];
}
