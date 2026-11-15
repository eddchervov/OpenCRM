using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenCRM.Infrastructure.Data;
using Notifications.Contracts;

namespace OpenCRM.Modules.Notifications;

/// <summary> Диспетчер отправки исходящих уведомлений через RabbitMQ. </summary>
public sealed class NotificationOutboxDispatcher(
    OpenCRMContext context,
    ISendEndpointProvider endpoints,
    IOptions<RabbitMqOptions> options)
{
    /// <summary> Отправляет следующее сообщение из outbox, возвращает успех. </summary>
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

    /// <summary> Внутренняя реализация отправки следующего сообщения из outbox. </summary>
    private async Task<bool> DispatchCoreAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var messages = await context.EmailMessages.FromSqlInterpolated($"""
                SELECT * FROM "EmailMessages"
                WHERE "NotificationKey" IS NOT NULL AND "NextDispatchAt" <= {now}
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

        var endpoint = await endpoints.GetSendEndpoint(new Uri($"queue:{NotificationQueues.Commands(options.Value.ApplicationId)}"));
        var commandId = Guid.NewGuid();

        if (message.NotificationDispatchedAt == null)
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(message.TemplateDataJson!);

            var createNotificationRequest = new CreateNotificationRequest(
                    message.NotificationKey!,
                    "email",
                    message.ToEmail,
                    message.TemplateKey,
                    data,
                    null,
                    null,
                    null);

            var sendNotification = new SendNotification(createNotificationRequest);

            await endpoint.Send(
                sendNotification,
                send => send.MessageId = commandId,
                timeout.Token);

            message.NotificationDispatchedAt = now;
        }
        else
        {
            await endpoint.Send(new GetNotificationStatus(message.NotificationKey!),
                send => send.MessageId = commandId, timeout.Token);
        }

        // После сбоя между Send и SaveChanges команда повторится с прежним ключом идемпотентности.
        message.NotificationCommandId = commandId;
        message.NextDispatchAt = now.AddSeconds(options.Value.ResponseTimeoutSeconds);

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
