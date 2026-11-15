namespace OpenCRM.Modules.Communications.Contracts.Dtos;

public sealed record PreviewMessageTemplateDto
{
    public Guid ConversationId { get; init; }
    public Guid TemplateId { get; init; }
    public int TemplateVersion { get; init; }
    public Guid? MaintenanceId { get; init; }
}
