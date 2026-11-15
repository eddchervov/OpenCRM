using MassTransit;
using Microsoft.Extensions.Options;
using Notifications.Contracts;

namespace Notifications.Infrastructure;

/// <summary>Принимает отдельные адресные wakeup-команды для Telegram-переписок.</summary>
public sealed class TelegramChatDeliveryConsumer(
    ConversationDeliveryInboxStore inboxStore,
    IOptions<NotificationMessagingOptions> messagingOptions)
    : IConsumer<ConversationDeliveryWakeup>, IConsumer<SendTelegramChatMessage>
{
    /// <inheritdoc />
    public Task Consume(ConsumeContext<ConversationDeliveryWakeup> context)
    {
        var route = messagingOptions.Value.GetConversationDeliveryRoute(context.ReceiveContext.InputAddress);
        if (context.Message.ProviderCode != route.ProviderCode)
        {
            throw new InvalidOperationException("Conversation delivery provider does not match its RabbitMQ queue.");
        }

        return inboxStore.EnqueueAsync(
            context.Message.ProviderCode,
            context.Message.MessageId,
            context.Message.RequestId,
            context.CancellationToken);
    }

    /// <summary>Принимает wakeup старого формата Telegram до завершения перехода отправителя.</summary>
    public Task Consume(ConsumeContext<SendTelegramChatMessage> context)
    {
        return inboxStore.EnqueueAsync(
            ConversationDeliveryProvider.Telegram,
            context.Message.MessageId,
            context.Message.RequestId,
            context.CancellationToken);
    }
}
