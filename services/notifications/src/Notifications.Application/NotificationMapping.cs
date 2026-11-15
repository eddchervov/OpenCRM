using System.Text.Json;

using Notifications.Contracts;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>
/// Содержит преобразования между внешними контрактами и доменной моделью.
/// </summary>
public static class NotificationMapping
{
    /// <summary>Формирует безопасный внешний снимок состояния уведомления.</summary>
    public static NotificationResponse ToResponse(this Notification notification, bool wasDuplicate = false)
    {
        return new NotificationResponse(
            notification.Id,
            notification.ApplicationId,
            notification.Channel == NotificationChannel.TelegramClient
                ? NotificationChannels.TelegramClient
                : notification.Channel.ToString().ToLowerInvariant(),
            notification.Recipient,
            notification.Status.ToString().ToLowerInvariant(),
            notification.AttemptCount,
            notification.CreatedAt,
            notification.ScheduledAt,
            notification.SentAt,
            notification.LastError,
            wasDuplicate);
    }

    /// <summary>Возвращает состояние и сохранённое содержимое только в очередь приложения-владельца.</summary>
    public static NotificationStatusChanged ToStatusChanged(
        this Notification notification,
        DateTimeOffset occurredAt,
        string? providerMessageId = null)
    {
        providerMessageId ??= notification.Attempts
            .Where(attempt => attempt.Succeeded)
            .OrderByDescending(attempt => attempt.Number)
            .Select(attempt => attempt.ProviderMessageId)
            .FirstOrDefault();

        var status = notification.Status switch
        {
            NotificationStatus.Sent when providerMessageId?.StartsWith("log:", StringComparison.Ordinal) == true => "simulated",
            NotificationStatus.Sent => "provider-accepted",
            NotificationStatus.Failed => "failed",
            NotificationStatus.Processing => "processing",
            _ when notification.AttemptCount > 0 => "retrying",
            _ => "queued"
        };

        return new NotificationStatusChanged(
            Guid.NewGuid(),
            notification.ApplicationId,
            notification.IdempotencyKey,
            notification.Id,
            status,
            notification.AttemptCount,
            occurredAt,
            providerMessageId,
            notification.LastError == null ? null : "delivery-failed",
            notification.IsHtml.HasValue && notification.Body != null
                ? new NotificationContent(notification.Subject, notification.Body, notification.IsHtml.Value,
                    notification.IsHtml.Value ? NotificationText.FromHtml(notification.Body) : notification.Body)
                : null);
    }

    /// <summary>Преобразует строковый канал сообщения в строго типизированное доменное значение.</summary>
    public static NotificationChannel ParseChannel(string channel)
    {
        return channel.Trim().ToLowerInvariant() switch
        {
            NotificationChannels.Email => NotificationChannel.Email,
            NotificationChannels.Sms => NotificationChannel.Sms,
            NotificationChannels.Telegram => NotificationChannel.Telegram,
            NotificationChannels.TelegramClient => NotificationChannel.TelegramClient,
            NotificationChannels.Max => NotificationChannel.Max,
            _ => throw new ArgumentException($"Channel '{channel}' is not supported.")
        };
    }
}
