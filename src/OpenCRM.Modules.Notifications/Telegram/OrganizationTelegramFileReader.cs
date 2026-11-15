using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace OpenCRM.Modules.Notifications.Telegram;

/// <summary>Скачивает файл через relay после проверки текущей привязки Telegram-бота.</summary>
public sealed class OrganizationTelegramFileReader(
    IHttpClientFactory httpClientFactory,
    IOrganizationTelegramCredentialReader credentialReader,
    IConfiguration configuration) : IOrganizationTelegramFileReader
{
    private const long MaxFileBytes = 20 * 1024 * 1024;

    /// <inheritdoc />
    public async Task<TelegramFileDownload?> OpenAsync(
        Guid organizationId,
        long telegramBotId,
        string providerFileId,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty
            || telegramBotId <= 0
            || string.IsNullOrEmpty(providerFileId)
            || providerFileId.Length is < 10 or > 512
            || providerFileId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return null;
        }

        var credential = await credentialReader.GetActiveCredentialAsync(
            organizationId,
            telegramBotId,
            cancellationToken);
        if (credential == null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "getOrganizationFile")
        {
            Content = JsonContent.Create(new { bot_token = credential.Token, file_id = providerFileId })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            configuration["TelegramOrganizationNotifications:RelayKey"]);
        var response = await httpClientFactory.CreateClient("TelegramOrganizationFileRelay").SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new HttpRequestException($"Telegram file relay returned HTTP {(int)response.StatusCode}.");
        }

        var sizeBytes = response.Content.Headers.ContentLength;
        if (sizeBytes is null or <= 0 or > MaxFileBytes)
        {
            response.Dispose();
            throw new InvalidDataException("Telegram file relay returned an invalid file size.");
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType is not null && contentType != "application/octet-stream")
        {
            response.Dispose();
            await stream.DisposeAsync();
            throw new InvalidDataException("Telegram file relay returned an unexpected content type.");
        }

        return new TelegramFileDownload(response, stream);
    }
}
