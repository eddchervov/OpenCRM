namespace OpenCRM.Modules.Notifications.Telegram;

/// <summary>Открывает файл по provider file_id только для текущего бота организации.</summary>
public interface IOrganizationTelegramFileReader
{
    Task<TelegramFileDownload?> OpenAsync(
        Guid organizationId,
        long telegramBotId,
        string providerFileId,
        CancellationToken cancellationToken = default);
}
