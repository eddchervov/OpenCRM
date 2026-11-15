namespace OpenCRM.Modules.Communications.Contracts.Dtos;

public sealed record MessageTemplatePreviewDto
{
    public Guid TemplateId { get; init; }
    public int TemplateVersion { get; init; }
    public string Text { get; init; } = string.Empty;
}
