namespace Notifications.Contracts;

/// <summary> Подтверждает результат обработки арендованной доставки канала. </summary>
public sealed record ConversationDeliveryCompleteRequest
{
    public Guid LeaseId { get; init; }
    public ConversationDeliveryResult Result { get; init; } = new();
}

/// <summary> Результат попытки, классифицированный транспортным адаптером провайдера. </summary>
public sealed record ConversationDeliveryResult
{
    public string Status { get; init; } = "uncertain";
    public string? ProviderMessageId { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool SafeToRetry { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public DateTime CompletedAt { get; init; }
}
