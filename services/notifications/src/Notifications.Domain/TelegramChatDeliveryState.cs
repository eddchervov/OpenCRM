namespace Notifications.Domain;

/// <summary>Сохраняет границу внешнего вызова Telegram для защиты от повторной отправки.</summary>
public sealed class TelegramChatDeliveryState
{
    public Guid LeaseId { get; set; }
    public Guid RequestId { get; set; }
    public Guid MessageId { get; set; }
    public string Status { get; set; } = "ready";
    public string? ProviderMessageId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool SafeToRetry { get; set; }
    public DateTimeOffset? RetryAfterAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
