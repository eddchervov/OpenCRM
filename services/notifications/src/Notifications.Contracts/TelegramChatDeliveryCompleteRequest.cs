namespace Notifications.Contracts;

/// <summary>Подтверждает исход обработки одной арендованной доставки Telegram.</summary>
public sealed record TelegramChatDeliveryCompleteRequest
{
    public Guid LeaseId { get; init; }
    public TelegramChatDeliveryResult Result { get; init; } = new();
}

/// <summary>Результат вызова Telegram Bot API, уже классифицированный transport worker.</summary>
public sealed record TelegramChatDeliveryResult
{
    public string Status { get; init; } = "uncertain";
    public string? ProviderMessageId { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool SafeToRetry { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public DateTime CompletedAt { get; init; }
}

/// <summary>Передаёт идентификатор запроса для захвата исходящего сообщения.</summary>
public sealed record TelegramChatDeliveryClaimRequest(Guid RequestId);
