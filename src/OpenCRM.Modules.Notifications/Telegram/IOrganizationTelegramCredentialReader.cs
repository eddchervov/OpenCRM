namespace OpenCRM.Modules.Notifications.Telegram;

/// <summary>Предоставляет внутренним API текущие учётные данные бота после проверки его идентификатора.</summary>
public interface IOrganizationTelegramCredentialReader
{
    Task<OrganizationTelegramCredential?> GetActiveCredentialAsync(
        Guid organizationId,
        long telegramBotId,
        CancellationToken cancellationToken = default);
}
