using MassTransit;
using Microsoft.Extensions.Options;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;
using Notifications.Contracts;

namespace OpenCRM.Modules.Notifications;

/// <summary>Публикует ограниченные wakeup-команды для сохранённых исходящих сообщений.</summary>
public sealed class ConversationDispatchDispatcher(
    IConversationDispatchOutbox outbox,
    ISendEndpointProvider endpoints,
    IOptions<RabbitMqOptions> options)
{
    private const int BatchSize = 16;
    private static readonly TimeSpan DispatchLeaseDuration = TimeSpan.FromMinutes(2);

    /// <summary>Публикует ожидающий пакет и фиксирует подтверждение брокера для каждой команды.</summary>
    public async Task<bool> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var batch = await outbox.ClaimPendingBatchAsync(BatchSize, DispatchLeaseDuration, cancellationToken);
        if (batch.Count == 0)
        {
            return false;
        }

        for (var index = 0; index < batch.Count; index++)
        {
            var item = batch[index];
            string? errorCode = null;
            var brokerAccepted = false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.SendTimeoutSeconds));
            try
            {
                var providerCode = item.ProviderCode switch
                {
                    "telegram" => ConversationDeliveryProvider.Telegram,
                    "max" => ConversationDeliveryProvider.Max,
                    _ => throw new InvalidOperationException("Core returned an unsupported conversation provider.")
                };
                var endpoint = await endpoints.GetSendEndpoint(
                    new Uri($"exchange:{NotificationQueues.ConversationDelivery(options.Value.ApplicationId, providerCode)}"));
                await endpoint.Send(
                    new ConversationDeliveryWakeup(providerCode, item.MessageId, item.RequestId),
                    send =>
                    {
                        send.MessageId = item.DispatchId;
                        send.GetPayload<RabbitMqSendContext>().Mandatory = true;
                        send.SetAwaitAck(true);
                    },
                    timeout.Token);
                brokerAccepted = true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                errorCode = "broker-timeout";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errorCode = exception.GetType().Name;
            }

            await outbox.CompletePublicationAsync(
                item.DispatchId,
                item.LeaseId,
                brokerAccepted,
                errorCode,
                cancellationToken);
            if (!brokerAccepted)
            {
                await this.ReleaseUnpublishedAsync(batch, index + 1, "batch-publication-deferred", cancellationToken);

                return false;
            }
        }

        return true;
    }

    private async Task ReleaseUnpublishedAsync(
        IReadOnlyList<ConversationDispatchLeaseDto> batch,
        int startIndex,
        string errorCode,
        CancellationToken cancellationToken)
    {
        for (var index = startIndex; index < batch.Count; index++)
        {
            var item = batch[index];
            await outbox.CompletePublicationAsync(
                item.DispatchId,
                item.LeaseId,
                false,
                errorCode,
                cancellationToken);
        }
    }
}
