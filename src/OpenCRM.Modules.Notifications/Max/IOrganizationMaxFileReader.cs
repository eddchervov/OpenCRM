namespace OpenCRM.Modules.Notifications.Max;

/// <summary> Скачивает вложение MAX по внутренней ссылке на сообщение и индексу файла. </summary>
public interface IOrganizationMaxFileReader
{
    Task<OrganizationChannelFileDownload?> OpenAsync(
        Guid organizationId,
        Guid connectionId,
        string providerFileId,
        CancellationToken cancellationToken = default);
}
