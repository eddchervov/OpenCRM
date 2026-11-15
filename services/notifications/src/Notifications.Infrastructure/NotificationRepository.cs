using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Notifications.Application;
using Notifications.Domain;

namespace Notifications.Infrastructure;

/// <summary>
/// Реализует хранение уведомлений и шаблонов через Entity Framework Core.
/// </summary>
public sealed class NotificationRepository(NotificationDbContext context)
    : INotificationRepository, INotificationTemplateRepository
{
    /// <summary>Загружает уведомление вместе с журналом попыток.</summary>
    public Task<Notification?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return context.Notifications
            .Include(item => item.Attempts)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    /// <summary>Атомарно переводит доступное уведомление в обработку и увеличивает номер попытки.</summary>
    public async Task<Notification?> ClaimAsync(
        Guid id,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Условный UPDATE выполняет роль распределённой блокировки без удержания транзакции.
        var affectedRows = await context.Notifications
            .Where(item => item.Id == id)
            .Where(item => item.Status == NotificationStatus.Queued)
            .Where(item => item.ScheduledAt == null || item.ScheduledAt <= now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Status, NotificationStatus.Processing)
                    .SetProperty(item => item.ProcessingStartedAt, now)
                    .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1),
                cancellationToken);

        if (affectedRows == 0)
        {
            return null;
        }

        // Очистка исключает возврат устаревшей сущности, оставшейся в change tracker.
        context.ChangeTracker.Clear();

        return await this.GetAsync(id, cancellationToken);
    }

    /// <summary>Ищет уведомление по уникальному ключу клиентского приложения.</summary>
    public Task<Notification?> GetByIdempotencyKeyAsync(
        string applicationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return context.Notifications.Include(item => item.Attempts).SingleOrDefaultAsync(
            item => item.ApplicationId == applicationId && item.IdempotencyKey == idempotencyKey,
            cancellationToken);
    }

    /// <summary>Добавляет уведомление в контекст.</summary>
    public void Add(Notification notification)
    {
        context.Notifications.Add(notification);
    }

    /// <summary>Добавляет запись попытки в контекст.</summary>
    public void AddAttempt(NotificationAttempt attempt)
    {
        context.NotificationAttempts.Add(attempt);
    }

    /// <summary>Ищет точную версию шаблона для приложения, ключа и канала.</summary>
    public Task<NotificationTemplate?> GetAsync(
        string applicationId,
        string key,
        NotificationChannel channel,
        CancellationToken cancellationToken,
        int version = 1)
    {
        return context.NotificationTemplates.SingleOrDefaultAsync(
            item => item.ApplicationId == applicationId
                && item.Key == key
                && item.Channel == channel
                && item.Version == version,
            cancellationToken);
    }

    /// <summary>Добавляет шаблон в контекст.</summary>
    public void Add(NotificationTemplate notificationTemplate)
    {
        context.NotificationTemplates.Add(notificationTemplate);
    }
}
