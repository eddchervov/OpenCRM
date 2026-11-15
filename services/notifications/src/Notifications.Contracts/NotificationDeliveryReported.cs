namespace Notifications.Contracts;

/// <summary>Нормализованное событие провайдера для журнала и согласий приложения.</summary>
public sealed record NotificationDeliveryReported(
    Guid EventId,
    string ApplicationId,
    string IdempotencyKey,
    string Status,
    DateTimeOffset OccurredAt,
    string? Recipient,
    string? ProviderMessageId,

    string? Url = null,
    string? IpAddress = null,
    string? UserAgent = null,
    string? Details = null);
