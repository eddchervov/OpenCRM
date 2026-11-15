using System.Globalization;
using OpenCRM.Modules.Notifications.Max;
using OpenCRM.Modules.Notifications.Telegram;

namespace OpenCRM.Modules.Notifications;

/// <summary> Направляет загрузку файла в адаптер активного провайдера канала. </summary>
public sealed class OrganizationChannelFileReader(
    IOrganizationChannelCredentialReader credentials,
    IOrganizationTelegramFileReader telegramFiles,
    IOrganizationMaxFileReader maxFiles) : IOrganizationChannelFileReader
{
    public async Task<OrganizationChannelFileDownload?> OpenAsync(
        Guid organizationId,
        Guid connectionId,
        string providerFileId,
        CancellationToken cancellationToken = default)
    {
        var credential = await credentials.GetActiveCredentialAsync(organizationId, connectionId, cancellationToken);
        if (credential is null)
        {
            return null;
        }

        if (credential.ProviderCode == "telegram"
            && long.TryParse(credential.ProviderBotId, NumberStyles.None, CultureInfo.InvariantCulture, out var telegramBotId))
        {
            var download = await telegramFiles.OpenAsync(organizationId, telegramBotId, providerFileId, cancellationToken);
            return download is null
                ? null
                : new OrganizationChannelFileDownload(
                    download.Content,
                    download.SizeBytes,
                    download.ContentType,
                    download);
        }

        if (credential.ProviderCode == "max")
        {
            return await maxFiles.OpenAsync(organizationId, connectionId, providerFileId, cancellationToken);
        }

        return null;
    }
}
