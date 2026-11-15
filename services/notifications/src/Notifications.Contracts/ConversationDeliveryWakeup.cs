namespace Notifications.Contracts;

/// <summary>Адресный сигнал о готовности доставить сохранённое сообщение переписки.</summary>
public sealed record ConversationDeliveryWakeup(
    ConversationDeliveryProvider ProviderCode,
    Guid MessageId,
    Guid RequestId);

/// <summary>Каналы клиентской переписки, поддерживаемые транспортом.</summary>
public enum ConversationDeliveryProvider
{
    Telegram,
    Max
}
