namespace Notifications.Contracts;

/// <summary>Определяет стабильные адреса очередей интеграции.</summary>
public static class NotificationQueues
{
    /// <summary>Возвращает очередь команд приложения.</summary>
    public static string Commands(string applicationId)
    {
        ValidateApplicationId(applicationId);

        return $"notifications-{applicationId}-commands";
    }

    /// <summary>Возвращает очередь результатов приложения.</summary>
    public static string Results(string applicationId)
    {
        ValidateApplicationId(applicationId);

        return $"notifications-{applicationId}-results";
    }

    /// <summary>Возвращает очередь адресных сообщений для разговоров с клиентами в Telegram.</summary>
    public static string TelegramChat(string applicationId)
    {
        ValidateApplicationId(applicationId);

        return $"notifications-{applicationId}-telegram-chat";
    }

    /// <summary>Возвращает отдельную bounded очередь wakeup-сообщений канала переписки.</summary>
    public static string ConversationDelivery(
        string applicationId,
        ConversationDeliveryProvider providerCode)
    {
        ValidateApplicationId(applicationId);
        var provider = providerCode switch
        {
            ConversationDeliveryProvider.Telegram => "telegram",
            ConversationDeliveryProvider.Max => "max",
            _ => throw new ArgumentOutOfRangeException(nameof(providerCode))
        };

        return $"notifications-{applicationId}-conversation-{provider}";
    }

    /// <summary> Проверяет корректность идентификатора приложения. </summary>
    private static void ValidateApplicationId(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        if (applicationId.Length > 100
            || !applicationId.All(character => char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character) || character == '-'))
        {
            throw new ArgumentException("ApplicationId must contain 1-100 lowercase ASCII letters, digits or hyphens.");
        }
    }
}
