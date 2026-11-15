namespace OpenCRM.Modules.Communications.Contracts.Dtos;

public sealed record MessageTemplateDto
{
    public Guid Id { get; init; }
    public Guid GroupId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public int Version { get; init; }
    public bool IsArchived { get; init; }
}
