using Notifications.Contracts;

namespace Notifications.Infrastructure;

/// <summary>Определяет разрешённые приложения и сопоставляет их с входными очередями.</summary>
public sealed class NotificationMessagingOptions
{
    /// <summary>Приложения, для которых Worker создаёт очереди команд.</summary>
    public string[] Applications { get; set; } = [];

    /// <summary>Единственное приложение, которому разрешены очереди клиентских переписок.</summary>
    public string? ConversationApplicationId { get; set; }

    /// <summary>Определяет владельца по адресу очереди, который задаётся транспортом.</summary>
    public string GetApplicationId(Uri inputAddress)
    {
        var queue = Uri.UnescapeDataString(inputAddress.AbsolutePath.Split('/')[^1]);

        return this.Applications.Single(applicationId => NotificationQueues.Commands(applicationId) == queue);
    }

    /// <summary>Определяет приложение по адресу отдельной очереди переписок.</summary>
    public string GetTelegramChatApplicationId(Uri inputAddress)
    {
        var queue = Uri.UnescapeDataString(inputAddress.AbsolutePath.Split('/')[^1]);
        var applicationId = this.ConversationApplicationId;
        if (!string.IsNullOrWhiteSpace(applicationId)
            && NotificationQueues.TelegramChat(applicationId) == queue)
        {
            return applicationId;
        }

        throw new InvalidOperationException("Telegram chat queue is not registered for the selected application.");
    }

    /// <summary>Проверяет приложение и канал по адресу очереди доставки переписки.</summary>
    public (string ApplicationId, ConversationDeliveryProvider ProviderCode) GetConversationDeliveryRoute(
        Uri inputAddress)
    {
        var queue = Uri.UnescapeDataString(inputAddress.AbsolutePath.Split('/')[^1]);
        var applicationId = this.ConversationApplicationId;
        if (!string.IsNullOrWhiteSpace(applicationId))
        {
            foreach (var providerCode in Enum.GetValues<ConversationDeliveryProvider>())
            {
                if (NotificationQueues.ConversationDelivery(applicationId, providerCode) == queue)
                {
                    return (applicationId, providerCode);
                }
            }
        }

        throw new InvalidOperationException("Conversation delivery queue is not registered for an application.");
    }
}
