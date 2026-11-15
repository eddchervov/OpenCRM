using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenCRM.Infrastructure.Data;
using Notifications.Contracts;

namespace OpenCRM.Modules.Notifications;

/// <summary> Диспетчер отправки исходящих Telegram-уведомлений через RabbitMQ. </summary>
public sealed class TelegramNotificationOutboxDispatcher(
    OpenCRMContext context,
    ISendEndpointProvider endpoints,
    IOptions<RabbitMqOptions> options)
{
    /// <summary> Отправляет следующее Telegram-сообщение из outbox, возвращает успех. </summary>
    public async Task<bool> DispatchNextAsync(CancellationToken cancellationToken)
    {
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            bool dispatched;
            await using (transaction.ConfigureAwait(true))
            {
                dispatched = await this.DispatchCoreAsync(cancellationToken);
                if (dispatched)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            }

            return dispatched;
        });
    }

    /// <summary> Внутренняя реализация отправки следующего Telegram-сообщения. </summary>
    private async Task<bool> DispatchCoreAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var messages = await context.TelegramNotificationOutbox.FromSqlInterpolated($"""
                SELECT * FROM "TelegramNotificationOutbox"
                WHERE "NextDispatchAt" <= {now}
                  AND ("NotificationStatus" IS NULL OR "NotificationStatus" NOT IN ('provider-accepted', 'simulated', 'failed'))
                ORDER BY "NextDispatchAt", "Id"
                LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(cancellationToken);

        var message = messages.SingleOrDefault();
        if (message == null)
        {
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.SendTimeoutSeconds));
        var endpoint = await endpoints.GetSendEndpoint(
            new Uri($"queue:{NotificationQueues.Commands(options.Value.ApplicationId)}"));
        var commandId = Guid.NewGuid();

        if (message.NotificationDispatchedAt == null)
        {
            var request = new CreateNotificationRequest(
                message.NotificationKey,
                NotificationChannels.Telegram,
                "owner",
                null,
                null,
                null,
                message.Body,
                null);
            await endpoint.Send(new SendNotification(request),
                send => send.MessageId = commandId, timeout.Token);
            message.NotificationDispatchedAt = now;
        }
        else
        {
            await endpoint.Send(new GetNotificationStatus(message.NotificationKey),
                send => send.MessageId = commandId, timeout.Token);
        }

        message.NotificationCommandId = commandId;
        message.NextDispatchAt = now.AddSeconds(options.Value.ResponseTimeoutSeconds);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
