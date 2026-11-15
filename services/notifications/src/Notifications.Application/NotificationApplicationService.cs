using System.Text.Json;
using Notifications.Contracts;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>Регистрирует уведомления и возвращает состояния через очередь приложения.</summary>
public sealed class NotificationApplicationService(
    INotificationRepository notificationRepository,
    NotificationContentResolver contentResolver,
    IIntegrationEventPublisher integrationEventPublisher,
    IUnitOfWork unitOfWork,
    ISystemClock clock)
{
    /// <summary>Сохраняет уведомление, подтверждение и задание доставки в одной транзакции.</summary>
    public async Task<NotificationResponse> EnqueueAsync(
        string applicationId,
        CreateNotificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateKey(applicationId, 100);
        ValidateKey(request.IdempotencyKey, 200);
        ValidateKey(request.Recipient, 500);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Channel);
        if (request.TemplateKey?.Trim().Length > 200 || request.Subject?.Length > 1000)
        {
            throw new ArgumentException("TemplateKey or Subject exceeds the allowed length.");
        }

        applicationId = applicationId.Trim();
        var idempotencyKey = request.IdempotencyKey.Trim();
        var existing = await notificationRepository.GetByIdempotencyKeyAsync(
            applicationId, idempotencyKey, cancellationToken);

        if (existing != null)
        {
            await this.SendStatusAsync(existing, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return existing.ToResponse(wasDuplicate: true);
        }

        var channel = NotificationMapping.ParseChannel(request.Channel);
        var dataJson = JsonSerializer.Serialize(request.Data ?? new Dictionary<string, JsonElement>());
        var notification = Notification.Create(
            applicationId,
            idempotencyKey,
            channel,
            request.Recipient,
            request.TemplateKey,
            dataJson,
            request.Subject,
            request.Body,
            clock.UtcNow,
            request.ScheduledAt?.ToUniversalTime());

        var content = await contentResolver.ResolveAsync(notification, cancellationToken, request.TemplateVersion);
        notification.CaptureContent(
            content.Subject, content.Body, content.IsHtml,
            string.IsNullOrWhiteSpace(notification.TemplateKey) ? null : request.TemplateVersion);
        notificationRepository.Add(notification);
        await this.SendStatusAsync(notification, cancellationToken);

        if (notification.ScheduledAt == null || notification.ScheduledAt <= clock.UtcNow)
        {
            notification.MarkDispatchRequested(clock.UtcNow);
            await integrationEventPublisher.PublishAsync(
                new NotificationDispatchRequested(notification.Id), cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return notification.ToResponse();
    }

    /// <summary>Возвращает снимок состояния или not-found в очередь приложения-владельца.</summary>
    public async Task ReportStatusAsync(
        string applicationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateKey(applicationId, 100);
        ValidateKey(idempotencyKey, 200);
        applicationId = applicationId.Trim();
        idempotencyKey = idempotencyKey.Trim();
        var notification = await notificationRepository.GetByIdempotencyKeyAsync(
            applicationId, idempotencyKey, cancellationToken);

        var status = notification == null
            ? new NotificationStatusChanged(
                Guid.NewGuid(), applicationId, idempotencyKey, null, "not-found", 0, clock.UtcNow)
            : notification.ToStatusChanged(clock.UtcNow);

        await integrationEventPublisher.SendToApplicationAsync(applicationId, status, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary> Обновляет статус отправки уведомления. </summary>
    private Task SendStatusAsync(Notification notification, CancellationToken cancellationToken)
    {
        return integrationEventPublisher.SendToApplicationAsync(
            notification.ApplicationId, notification.ToStatusChanged(clock.UtcNow), cancellationToken);
    }

    /// <summary> Проверяет корректность и длину значения ключа. </summary>
    private static void ValidateKey(string value, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Trim().Length > maximumLength)
        {
            throw new ArgumentException($"Value exceeds {maximumLength} characters.");
        }
    }
}
