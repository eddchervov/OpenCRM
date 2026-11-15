using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Notifications.Contracts;
using Notifications.Domain;

namespace Notifications.Infrastructure;

/// <summary>
/// Периодически публикует готовые отложенные уведомления и восстанавливает зависшую обработку.
/// </summary>
public sealed partial class ScheduledNotificationPublisher(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ScheduledNotificationPublisher> logger) : BackgroundService
{
    /// <summary>Запускает проверку очереди сразу, а затем повторяет её каждые десять секунд.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), timeProvider);

        do
        {
            try
            {
                await this.PublishDueNotificationsAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogPublishingFailure(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary> Логирует ошибку публикации отложенных уведомлений. </summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to publish scheduled notifications.")]
    private static partial void LogPublishingFailure(ILogger logger, Exception exception);

    /// <summary> Публикует уведомления, готовые к отправке. </summary>
    private async Task PublishDueNotificationsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var now = timeProvider.GetUtcNow();
        // Обработка дольше десяти минут считается прерванной после падения Worker-а.
        var expiredLease = now.AddMinutes(-10);

        await context.Notifications
            .Where(item => item.Status == NotificationStatus.Processing)
            .Where(item => item.ProcessingStartedAt < expiredLease)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Status, NotificationStatus.Queued)
                    .SetProperty(item => item.ProcessingStartedAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.DispatchRequestedAt, (DateTimeOffset?)null),
                cancellationToken);

        // Ограниченный пакет удерживает предсказуемое время транзакции и нагрузку на брокер.
        var notifications = await context.Notifications
            .Where(item => item.Status == NotificationStatus.Queued)
            .Where(item => item.DispatchRequestedAt == null)
            .Where(item => item.ScheduledAt == null || item.ScheduledAt <= now)
            .OrderBy(item => item.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            notification.MarkDispatchRequested(now);
            await publishEndpoint.Publish(
                new NotificationDispatchRequested(notification.Id),
                cancellationToken);
        }

        if (notifications.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
