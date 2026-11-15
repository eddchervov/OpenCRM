using Microsoft.Extensions.Logging;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>Доставляет сохранённое содержимое и атомарно фиксирует результат попытки.</summary>
public sealed partial class NotificationDispatcher(
    INotificationRepository notificationRepository,
    NotificationContentResolver contentResolver,
    IEnumerable<INotificationChannelSender> senders,
    IUnitOfWork unitOfWork,
    IIntegrationEventPublisher publisher,
    ISystemClock clock,
    ILogger<NotificationDispatcher> logger)
{
    /// <summary> Максимальное количество попыток доставки. </summary>
    private const int MaximumAttempts = 4;

    /// <summary>Захватывает уведомление и передаёт провайдеру снимок, одинаковый для всех попыток.</summary>
    public async Task DispatchAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        var notification = await notificationRepository.ClaimAsync(
            notificationId, clock.UtcNow, cancellationToken);

        if (notification == null)
        {
            LogNotificationSkipped(logger, notificationId);

            return;
        }

        RenderedNotificationContent content;
        try
        {
            if (notification.AttemptCount > MaximumAttempts)
            {
                throw new InvalidOperationException("Delivery attempt limit exceeded after recovery.");
            }

            content = await contentResolver.ResolveAsync(
                notification, cancellationToken, notification.TemplateVersion ?? 1);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await this.RecordFailureAsync(notification, exception, cancellationToken);
            if (notification.Status != NotificationStatus.Failed)
            {
                throw;
            }

            return;
        }

        if (!notification.IsHtml.HasValue)
        {
            // Старые задания получают снимок до первого обращения к провайдеру.
            notification.CaptureContent(
                content.Subject, content.Body, content.IsHtml,
                string.IsNullOrWhiteSpace(notification.TemplateKey) ? null : notification.TemplateVersion ?? 1);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        string? providerMessageId;
        try
        {
            var sender = senders.SingleOrDefault(item => item.Channel == notification.Channel)
                ?? throw new InvalidOperationException($"Sender '{notification.Channel}' is not registered.");

            providerMessageId = await sender.SendAsync(
                notification.Recipient, content.Subject, content.Body, content.IsHtml, cancellationToken, notification.IdempotencyKey, notification.ApplicationId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Прерванную попытку восстановит планировщик после истечения аренды.
            throw;
        }
        catch (Exception exception)
        {
            await this.RecordFailureAsync(notification, exception, cancellationToken);
            if (notification.Status != NotificationStatus.Failed)
            {
                throw;
            }

            return;
        }

        // Сбой БД после передачи провайдеру не превращается в ошибку провайдера.
        notification.MarkSent(clock.UtcNow);
        notificationRepository.AddAttempt(new NotificationAttempt(
            notification.Id, notification.AttemptCount, clock.UtcNow,
            succeeded: true, providerMessageId, error: null));
        await publisher.SendToApplicationAsync(
            notification.ApplicationId,
            notification.ToStatusChanged(clock.UtcNow, providerMessageId),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogDeliverySucceeded(
            logger, notification.Id, notification.ApplicationId, notification.Channel, notification.AttemptCount);
    }

    /// <summary> Фиксирует ошибку попытки доставки уведомления. </summary>
    private async Task RecordFailureAsync(
        Notification notification,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var error = exception.Message.Length > 2000 ? exception.Message[..2000] : exception.Message;
        if (notification.AttemptCount >= MaximumAttempts)
        {
            notification.MarkFailed(error);
            LogDeliveryFailed(
                logger, exception, notification.Id, notification.ApplicationId,
                notification.Channel, notification.AttemptCount);
        }
        else
        {
            notification.MarkQueuedForRetry(error);
            LogDeliveryAttemptFailed(
                logger, exception, notification.Id, notification.ApplicationId,
                notification.Channel, notification.AttemptCount);
        }

        notificationRepository.AddAttempt(new NotificationAttempt(
            notification.Id, notification.AttemptCount, clock.UtcNow,
            succeeded: false, providerMessageId: null, error));
        await publisher.SendToApplicationAsync(
            notification.ApplicationId, notification.ToStatusChanged(clock.UtcNow), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary> Логирует пропуск уведомления при обработке. </summary>
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Debug,
        Message = "Уведомление {NotificationId} пропущено: оно уже обрабатывается, завершено или ещё не готово к отправке.")]
    private static partial void LogNotificationSkipped(ILogger logger, Guid notificationId);

    /// <summary> Логирует успешную доставку уведомления. </summary>
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Уведомление {NotificationId} приложения {ApplicationId} успешно передано через {Channel} с попытки {AttemptNumber}.")]
    private static partial void LogDeliverySucceeded(
        ILogger logger,
        Guid notificationId,
        string applicationId,
        NotificationChannel channel,
        int attemptNumber);

    /// <summary> Логирует ошибку попытки доставки с повтором. </summary>
    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Попытка {AttemptNumber} доставки уведомления {NotificationId} приложения {ApplicationId} через {Channel} завершилась ошибкой и будет повторена.")]
    private static partial void LogDeliveryAttemptFailed(
        ILogger logger,
        Exception exception,
        Guid notificationId,
        string applicationId,
        NotificationChannel channel,
        int attemptNumber);

    /// <summary> Логирует окончательную неудачу доставки уведомления. </summary>
    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "Доставка уведомления {NotificationId} приложения {ApplicationId} через {Channel} окончательно завершилась ошибкой после {AttemptNumber} попыток.")]
    private static partial void LogDeliveryFailed(
        ILogger logger,
        Exception exception,
        Guid notificationId,
        string applicationId,
        NotificationChannel channel,
        int attemptNumber);
}
