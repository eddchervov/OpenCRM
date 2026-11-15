namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Сообщение диалога и связанные с ним вложения. </summary>
public sealed record ConversationMessageDto
{
    public Guid Id { get; init; }
    public long Sequence { get; init; }
    public ConversationMessageDirection Direction { get; init; }
    public ConversationMessageStatus Status { get; init; }
    public string? Text { get; init; }
    public string? ActorUserId { get; init; }
    public string? ActorDisplayName { get; init; }
    public Guid? MaintenanceId { get; init; }
    public string? MaintenanceTitle { get; init; }
    public string? ProviderMessageId { get; init; }
    public string? DeliveryErrorCode { get; init; }
    public DateTime? RetryAt { get; init; }
    public Guid? MessageTemplateId { get; init; }
    public int? MessageTemplateVersion { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public IReadOnlyList<ConversationAttachmentDto> Attachments { get; init; } = [];
}
