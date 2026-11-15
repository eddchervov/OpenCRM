namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Нормализованное входящее сообщение из проверенного webhook. </summary>
public sealed record ChannelMessageReceivedDto
{
    public Guid OrganizationId { get; init; }
    public Guid ConnectionId { get; init; }
    public string ProviderCode { get; init; } = string.Empty;
    public string ExternalConversationId { get; init; } = string.Empty;
    public string ExternalUserId { get; init; } = string.Empty;
    public string? DeliveryAddress { get; init; }
    public string? ProviderUpdateId { get; init; }
    public string ProviderMessageId { get; init; } = string.Empty;
    public string? SenderDisplayName { get; init; }
    public string? SenderUsername { get; init; }
    public string? Text { get; init; }
    public IReadOnlyList<ChannelAttachmentReceivedDto> Attachments { get; init; } = [];
    public DateTime OccurredAt { get; init; }
}
