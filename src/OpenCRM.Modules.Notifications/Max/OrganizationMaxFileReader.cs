using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using OpenCRM.Modules.Notifications;

namespace OpenCRM.Modules.Notifications.Max;

/// <summary> Получает MAX-файл через API по проверенной ссылке на сообщение, не отправляя токен CDN. </summary>
public sealed class OrganizationMaxFileReader(
    IHttpClientFactory httpClientFactory,
    IOrganizationChannelCredentialReader credentials) : IOrganizationMaxFileReader
{
    private const long MaxFileBytes = 20 * 1024 * 1024;

    public async Task<OrganizationChannelFileDownload?> OpenAsync(
        Guid organizationId,
        Guid connectionId,
        string providerFileId,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseFileReference(providerFileId, out var messageId, out var attachmentIndex))
        {
            return null;
        }

        var credential = await credentials.GetActiveCredentialAsync(organizationId, connectionId, cancellationToken);
        if (credential is null || credential.ProviderCode != "max")
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"messages/{Uri.EscapeDataString(messageId)}");
        request.Headers.TryAddWithoutValidation("Authorization", credential.Token);
        using var messageResponse = await httpClientFactory.CreateClient("MaxConversationApi").SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (messageResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!messageResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"MAX could not provide the message attachment metadata (HTTP {(int)messageResponse.StatusCode}).");
        }

        using var message = await JsonDocument.ParseAsync(
            await messageResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!TryGetAttachmentUrl(message.RootElement, attachmentIndex, out var fileUri))
        {
            return null;
        }

        using var fileResponse = await httpClientFactory.CreateClient("MaxConversationFile").GetAsync(
            fileUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (fileResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var sizeBytes = fileResponse.Content.Headers.ContentLength;
        if (!fileResponse.IsSuccessStatusCode || sizeBytes is null or <= 0 or > MaxFileBytes)
        {
            throw new InvalidDataException("MAX returned an invalid or oversized attachment.");
        }

        await using var source = await fileResponse.Content.ReadAsStreamAsync(cancellationToken);
        var content = new MemoryStream((int)sizeBytes.Value);
        try
        {
            var buffer = new byte[81920];
            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                if (content.Length + bytesRead > MaxFileBytes)
                {
                    throw new InvalidDataException("MAX attachment exceeds the configured size limit.");
                }

                await content.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }

            if (content.Length != sizeBytes.Value || content.Length > MaxFileBytes)
            {
                throw new InvalidDataException("MAX attachment size did not match its metadata.");
            }

            content.Position = 0;

            return new OrganizationChannelFileDownload(
                content,
                content.Length,
                fileResponse.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
                content);
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }
    }

    private static bool TryParseFileReference(string value, out string messageId, out int attachmentIndex)
    {
        messageId = string.Empty;
        attachmentIndex = 0;
        var separator = value.LastIndexOf(':');
        if (separator is < 1 or > 80
            || value.Length > 84
            || !int.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out attachmentIndex)
            || attachmentIndex is < 0 or > 11)
        {
            return false;
        }

        messageId = value[..separator];

        return messageId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }

    private static bool TryGetAttachmentUrl(JsonElement message, int index, out Uri uri)
    {
        uri = null!;
        if (!message.TryGetProperty("body", out var body)
            || body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("attachments", out var attachments)
            || attachments.ValueKind != JsonValueKind.Array
            || index >= attachments.GetArrayLength())
        {
            return false;
        }

        var attachment = attachments[index];
        if (!attachment.TryGetProperty("payload", out var payload)
            || payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("url", out var url)
            || url.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var parsedUri))
        {
            return false;
        }

        uri = parsedUri;

        return IsTrustedCdnUrl(uri);
    }

    private static bool IsTrustedCdnUrl(Uri uri) => uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.IsDefaultPort
        && (uri.Host.EndsWith(".okcdn.ru", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".oneme.ru", StringComparison.OrdinalIgnoreCase));

}
