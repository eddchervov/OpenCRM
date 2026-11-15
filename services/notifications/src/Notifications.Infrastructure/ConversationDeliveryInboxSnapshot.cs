namespace Notifications.Infrastructure;

/// <summary>Снимок задержек и объёма durable inbox канала переписки.</summary>
public sealed record ConversationDeliveryInboxSnapshot(
    int PendingCount,
    int DueCount,
    int ExpiredCount,
    DateTimeOffset? OldestPendingAt);
