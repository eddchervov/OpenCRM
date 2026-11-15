namespace OpenCRM.Modules.Notifications;

/// <summary> Открывает входящее вложение через проверенное подключение организации. </summary>
public interface IOrganizationChannelFileReader
{
    Task<OrganizationChannelFileDownload?> OpenAsync(
        Guid organizationId,
        Guid connectionId,
        string providerFileId,
        CancellationToken cancellationToken = default);
}

/// <summary> Ограниченный поток файла и владелец ответа транспортного провайдера. </summary>
public sealed class OrganizationChannelFileDownload(
    Stream content,
    long sizeBytes,
    string contentType,
    IAsyncDisposable owner) : IAsyncDisposable
{
    public Stream Content { get; } = content;
    public long SizeBytes { get; } = sizeBytes;
    public string ContentType { get; } = contentType;

    public async ValueTask DisposeAsync()
    {
        await owner.DisposeAsync();
    }
}
