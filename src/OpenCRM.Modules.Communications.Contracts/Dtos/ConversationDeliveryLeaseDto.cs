namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Сообщение с ограниченной по времени арендой для одного обработчика доставки. </summary>
public sealed record ConversationDeliveryLeaseDto
{
    public Guid MessageId { get; init; }
    public Guid LeaseId { get; init; }
    public bool IsRecovery { get; init; }
    public Guid OrganizationId { get; init; }
    public long? BotId { get; init; }
    public long? ChatId { get; init; }
    public string ProviderCode { get; init; } = string.Empty;
    public Guid ConnectionId { get; init; }
    public string ExternalConversationId { get; init; } = string.Empty;
    public string DeliveryAddress { get; init; } = string.Empty;
    public string BotUsername { get; init; } = string.Empty;
    public Guid RequestId { get; init; }
    public string? Text { get; init; }
    public IReadOnlyList<ConversationDeliveryAttachmentDto> Attachments { get; init; } = [];
}
