namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Запрос на постановку ответа сотрудника в очередь доставки. </summary>
public sealed record SendConversationMessageDto
{
    public Guid RequestId { get; init; }
    public string? Text { get; init; }
    public Guid? MaintenanceId { get; init; }
    public Guid? MessageTemplateId { get; init; }
    public int? MessageTemplateVersion { get; init; }
    public IReadOnlyList<Guid> AttachmentIds { get; init; } = [];
}
