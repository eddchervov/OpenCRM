namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Итог вызова Telegram Bot API для аренды сообщения. </summary>
public sealed record ConversationDeliveryResultDto
{
    public ConversationMessageStatus Status { get; init; }
    public string? ProviderMessageId { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool SafeToRetry { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public DateTime CompletedAt { get; init; }
}
