namespace Notifications.Contracts;

/// <summary>Адресное уведомление о новом исходящем сообщении в чате Telegram.</summary>
public sealed record SendTelegramChatMessage(Guid MessageId, Guid RequestId);
