using System.Collections.Concurrent;
using Notifications.Application;
using Notifications.Domain;

namespace Notifications.Tests.Integration;

/// <summary>Фиксирует вызовы провайдера и воспроизводит ошибку без внешней отправки.</summary>
public sealed class RecordingNotificationSender : INotificationChannelSender
{
    /// <summary>Содержимое фактических попыток для проверки шаблонов и повторов.</summary>
    public ConcurrentQueue<(string Recipient, string Body, bool IsHtml)> Calls { get; } = new();

    /// <summary>Поддерживаемый канал.</summary>
    public NotificationChannel Channel => NotificationChannel.Email;

    /// <summary>Имитирует принятие провайдером либо воспроизводимую ошибку.</summary>
    public Task<string?> SendAsync(
        string recipient, string? subject, string body, bool isHtml, CancellationToken cancellationToken, string? idempotencyKey = null, string? applicationId = null)
    {
        this.Calls.Enqueue((recipient, body, isHtml));
        if (recipient == "contact@example.invalid")
        {
            throw new InvalidOperationException("Test provider failure.");
        }

        return Task.FromResult<string?>($"log:{Guid.NewGuid():N}");
    }
}
