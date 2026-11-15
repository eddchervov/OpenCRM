using System.Text.Json;

using Notifications.Contracts;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>
/// Представляет адаптер доставки для одного канала уведомлений.
/// </summary>
public interface INotificationChannelSender
{
    /// <summary>Канал, который поддерживает реализация.</summary>
    NotificationChannel Channel { get; }

    /// <summary>Передаёт подготовленное сообщение внешнему провайдеру.</summary>
    /// <returns>Идентификатор сообщения у провайдера или диагностический ответ.</returns>
    Task<string?> SendAsync(
        string recipient,
        string? subject,
        string body,
        bool isHtml,
        CancellationToken cancellationToken,
        string? idempotencyKey = null, string? applicationId = null);
}
