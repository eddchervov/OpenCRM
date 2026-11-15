using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Notifications.Telegram;

/// <summary>Читает только учётные данные подключённого бота, совпадающего с разрешённым botId.</summary>
public sealed class OrganizationTelegramCredentialReader(
    OpenCRMContext context,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration) : IOrganizationTelegramCredentialReader
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("OpenCRM.OrganizationTelegramBot.v1");

    /// <inheritdoc />
    public async Task<OrganizationTelegramCredential?> GetActiveCredentialAsync(
        Guid organizationId,
        long telegramBotId,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("TelegramOrganizationNotifications:Enabled") || telegramBotId <= 0)
        {
            return null;
        }

        var bot = await (from configuredBot in context.OrganizationTelegramBots.AsNoTracking()
            join organization in context.Organizations.AsNoTracking()
                on configuredBot.OrganizationId equals organization.Id
            where configuredBot.OrganizationId == organizationId
                && configuredBot.TelegramBotId == telegramBotId
                && configuredBot.IsEnabled
                && organization.IsActive
                && !organization.IsDeleted
                && !organization.IsArchived
            select new { configuredBot.TelegramBotId, configuredBot.Username, configuredBot.ProtectedToken })
            .SingleOrDefaultAsync(cancellationToken);
        if (bot?.TelegramBotId is not { } botId)
        {
            return null;
        }

        return new OrganizationTelegramCredential(botId, bot.Username, this._protector.Unprotect(bot.ProtectedToken));
    }
}
