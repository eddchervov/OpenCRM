namespace OpenCRM.Modules.Notifications.Telegram;

/// <summary>Текущие учётные данные активного Telegram-бота организации.</summary>
public sealed record OrganizationTelegramCredential(long BotId, string Username, string Token);
