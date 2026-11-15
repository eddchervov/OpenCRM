using MassTransit;
using Notifications.Application;
using Notifications.Contracts;

namespace Notifications.Infrastructure;

/// <summary>Записывает внутренние события и адресные результаты в текущий transactional outbox.</summary>
public sealed class MassTransitIntegrationEventPublisher(
    IPublishEndpoint publishEndpoint,
    ISendEndpointProvider sendEndpointProvider) : IIntegrationEventPublisher
{
    /// <summary>Публикует внутреннее событие доставки.</summary>
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken) where T : class
    {
        return publishEndpoint.Publish(message, cancellationToken);
    }

    /// <summary>Направляет результат только в фиксированную очередь приложения-владельца.</summary>
    public async Task SendToApplicationAsync<T>(
        string applicationId,
        T message,
        CancellationToken cancellationToken) where T : class
    {
        var address = new Uri($"queue:{NotificationQueues.Results(applicationId)}");
        var endpoint = await sendEndpointProvider.GetSendEndpoint(address);
        await endpoint.Send(message, cancellationToken);
    }
}
