namespace Notifications.Infrastructure;

/// <summary>Отмечает временную задержку, для которой durable inbox выполнит ограниченный повтор.</summary>
public sealed class ConversationDeliveryDeferredException(string message, int? retryAfterSeconds = null)
    : Exception(message)
{
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}
