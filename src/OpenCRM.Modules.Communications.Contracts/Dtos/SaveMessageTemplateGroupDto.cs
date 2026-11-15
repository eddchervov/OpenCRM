namespace OpenCRM.Modules.Communications.Contracts.Dtos;

public sealed record SaveMessageTemplateGroupDto
{
    public Guid? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsArchived { get; init; }
    public int? ExpectedVersion { get; init; }
}
