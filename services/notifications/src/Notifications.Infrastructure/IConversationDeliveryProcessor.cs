using Notifications.Contracts;

namespace Notifications.Infrastructure;

/// <summary>Обрабатывает адресную доставку одного подключённого канала переписки.</summary>
public interface IConversationDeliveryProcessor
{
    ConversationDeliveryProvider ProviderCode { get; }

    Task ProcessAsync(ConversationDeliveryWakeup command, CancellationToken cancellationToken);
}
