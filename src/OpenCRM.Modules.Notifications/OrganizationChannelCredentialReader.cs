using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Notifications;

/// <summary> Проверяет актуальную идентичность канала и извлекает его токен из хранилища организации. </summary>
public sealed class OrganizationChannelCredentialReader(
    OpenCRMContext context,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<OrganizationChannelCredentialReader> logger) : IOrganizationChannelCredentialReader
{
    public async Task<OrganizationChannelCredential?> GetActiveCredentialAsync(
        Guid organizationId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var connection = await context.OrganizationChannelConnections.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId
                && item.Id == connectionId && item.IsCurrent, cancellationToken);
        if (connection is null)
        {
            return null;
        }

        if (connection.ProviderCode == "telegram")
        {
            if (!long.TryParse(
                connection.ImmutableProviderIdentity,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var providerBotId))
            {
                return null;
            }

            var bot = await context.OrganizationTelegramBots.AsNoTracking()
                .SingleOrDefaultAsync(item => item.OrganizationId == organizationId
                    && item.IsEnabled && item.TelegramBotId.HasValue
                    && item.TelegramBotId.Value == providerBotId, cancellationToken);
            if (bot is null)
            {
                return null;
            }

            var protector = dataProtectionProvider.CreateProtector("OpenCRM.OrganizationTelegramBot.v1");
            var token = this.TryUnprotect(
                protector,
                bot.ProtectedToken,
                organizationId,
                connectionId,
                connection.ProviderCode);
            if (token is null)
            {
                return null;
            }

            return new OrganizationChannelCredential(
                connection.ProviderCode,
                connection.ImmutableProviderIdentity,
                token,
                bot.Username);
        }

        if (connection.ProviderCode != "max")
        {
            return null;
        }

        var maxBot = await context.OrganizationMaxBots.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId
                && item.IsEnabled && item.ImmutableProviderBotId == connection.ImmutableProviderIdentity, cancellationToken);
        if (maxBot is null)
        {
            return null;
        }

        var maxProtector = dataProtectionProvider.CreateProtector("OpenCRM.OrganizationMaxBot.v1");
        var maxToken = this.TryUnprotect(
            maxProtector,
            maxBot.ProtectedToken,
            organizationId,
            connectionId,
            connection.ProviderCode);
        if (maxToken is null)
        {
            return null;
        }

        return new OrganizationChannelCredential(
            connection.ProviderCode,
            connection.ImmutableProviderIdentity,
            maxToken,
            maxBot.Username);
    }

    private string? TryUnprotect(
        IDataProtector protector,
        string protectedToken,
        Guid organizationId,
        Guid connectionId,
        string providerCode)
    {
        try
        {
            return protector.Unprotect(protectedToken);
        }
        catch (CryptographicException exception)
        {
            logger.LogWarning(
                exception,
                "Не удалось расшифровать учётные данные канала {ProviderCode} организации {OrganizationId}, подключение {ConnectionId}.",
                providerCode,
                organizationId,
                connectionId);

            return null;
        }
    }
}
