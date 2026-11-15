using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Notifications.Contracts;

namespace Notifications.Infrastructure;

/// <summary> Передаёт сообщения клиентских переписок через MAX с сохранением границы внешней попытки. </summary>
public sealed class MaxChatDeliveryProcessor(
    IHttpClientFactory httpClientFactory,
    ConversationDeliveryAttemptStore stateStore,
    IMaxConversationRateLimiter rateLimiter,
    IOptions<MaxChatOptions> options) : IConversationDeliveryProcessor
{
    private const long MaxAttachmentBytes = 20 * 1024 * 1024;

    public ConversationDeliveryProvider ProviderCode => ConversationDeliveryProvider.Max;

    public async Task ProcessAsync(ConversationDeliveryWakeup command, CancellationToken cancellationToken)
    {
        if (command.ProviderCode != this.ProviderCode)
        {
            throw new InvalidOperationException("MAX delivery processor received a different provider.");
        }

        var delivery = await this.ClaimAsync(command, cancellationToken);
        if (delivery is null)
        {
            return;
        }

        if (!IsValidDelivery(command, delivery))
        {
            throw new InvalidOperationException("Core API returned an invalid MAX delivery claim.");
        }

        var attempt = await stateStore.BeginAsync(
            delivery.LeaseId,
            delivery.RequestId,
            delivery.MessageId,
            cancellationToken);
        ConversationDeliveryResult result;
        if (attempt.Status == "ready")
        {
            result = await this.SendAsync(delivery, cancellationToken);
            result = await this.SaveResultOrRestoreAsync(delivery.LeaseId, result, cancellationToken);
        }
        else if (attempt.Status == "started")
        {
            var providerDeadline = attempt.StartedAt?.AddSeconds(options.Value.RequestTimeoutSeconds + 5);
            if (providerDeadline is null || providerDeadline > DateTimeOffset.UtcNow)
            {
                throw new ConversationDeliveryDeferredException("Предыдущая отправка MAX ещё может завершиться.");
            }

            result = new ConversationDeliveryResult
            {
                Status = "uncertain",
                ErrorCode = "provider-result-unknown",
                ErrorMessage = "MAX не подтвердил результат предыдущей отправки.",
                CompletedAt = DateTime.UtcNow
            };
            result = await this.SaveResultOrRestoreAsync(delivery.LeaseId, result, cancellationToken);
        }
        else
        {
            result = RestoreResult(attempt);
        }

        await this.CompleteAsync(delivery, result, cancellationToken);
        if (result.Status == "failed" && result.SafeToRetry)
        {
            throw new ConversationDeliveryDeferredException(
                "Провайдер сообщил, что доставку можно безопасно повторить.",
                result.RetryAfterSeconds);
        }
    }

    private async Task<ConversationDeliveryClaimResponse?> ClaimAsync(
        ConversationDeliveryWakeup command,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"api/internal/conversation-deliveries/{command.MessageId:D}/claim")
        {
            Content = JsonContent.Create(new ConversationDeliveryClaimRequest(command.RequestId))
        };
        request.Headers.TryAddWithoutValidation("X-Notification-Service-Key", options.Value.ServiceKey);

        using var response = await this.CreateOpenCRMClient().SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.NoContent)
        {
            return null;
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta is { } delay
                ? (int)Math.Ceiling(delay.TotalSeconds)
                : (int?)null;
            throw new ConversationDeliveryDeferredException("Доставка уже захвачена другим обработчиком.", retryAfter);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Core API did not provide MAX delivery (HTTP {(int)response.StatusCode}).");
        }

        return await response.Content.ReadFromJsonAsync<ConversationDeliveryClaimResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Core API returned an empty MAX delivery claim.");
    }

    private static bool IsValidDelivery(
        ConversationDeliveryWakeup command,
        ConversationDeliveryClaimResponse delivery)
    {
        return delivery.MessageId == command.MessageId
            && delivery.RequestId == command.RequestId
            && delivery.LeaseId != Guid.Empty
            && delivery.OrganizationId != Guid.Empty
            && delivery.ProviderCode == ConversationDeliveryProvider.Max
            && delivery.ConnectionId != Guid.Empty
            && long.TryParse(delivery.ProviderBotId, NumberStyles.None, CultureInfo.InvariantCulture, out var providerBotId)
            && providerBotId > 0
            && long.TryParse(delivery.DeliveryAddress, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
            && userId > 0
            && !string.IsNullOrWhiteSpace(delivery.Token)
            && delivery.Attachments.Count <= 1
            && (delivery.Text?.Length ?? 0) <= 4000
            && delivery.Attachments.All(attachment => attachment.SizeBytes is > 0 and <= MaxAttachmentBytes
                && attachment.AttachmentId != Guid.Empty
                && attachment.Kind is "image" or "photo" or "video" or "audio" or "file" or "document"
                && !string.IsNullOrWhiteSpace(attachment.FileName)
                && attachment.FileName.Length <= 255
                && Path.GetFileName(attachment.FileName) == attachment.FileName
                && !attachment.FileName.Any(char.IsControl)
                && MediaTypeHeaderValue.TryParse(attachment.ContentType, out _));
    }

    private async Task<ConversationDeliveryResult> SendAsync(
        ConversationDeliveryClaimResponse delivery,
        CancellationToken cancellationToken)
    {
        byte[]? fileBytes = null;
        if (delivery.Attachments.Count == 1)
        {
            try
            {
                fileBytes = await this.DownloadAttachmentAsync(delivery, delivery.Attachments[0], cancellationToken);
            }
            catch (HttpRequestException)
            {
                return SafeRetry("attachment-unavailable", "Не удалось получить вложение для отправки.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return SafeRetry("attachment-download-timeout", "Получение вложения для MAX не завершилось вовремя.");
            }
        }

        await using var rateLimitLease = await rateLimiter.AcquireConversationAsync(
            $"{delivery.ProviderBotId}:{delivery.DeliveryAddress}",
            cancellationToken);
        object? attachments = null;
        try
        {
            attachments = fileBytes is null
                ? null
                : await this.UploadAttachmentAsync(delivery, delivery.Attachments[0], fileBytes, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return SafeRetry("attachment-upload-failed", "Не удалось загрузить вложение в MAX.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SafeRetry("attachment-upload-timeout", "Загрузка вложения в MAX не завершилась вовремя.");
        }
        catch (InvalidOperationException)
        {
            return SafeRetry("attachment-upload-invalid-response", "MAX вернул некорректный ответ на загрузку вложения.");
        }
        catch (JsonException)
        {
            return SafeRetry("attachment-upload-invalid-response", "MAX вернул некорректный ответ на загрузку вложения.");
        }

        await rateLimiter.WaitForGlobalRequestSlotAsync(cancellationToken);
        if (!await stateStore.TryMarkProviderStartedAsync(delivery.LeaseId, cancellationToken))
        {
            throw new ConversationDeliveryDeferredException("Другой обработчик уже начал отправку этого сообщения.");
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"messages?user_id={Uri.EscapeDataString(delivery.DeliveryAddress)}")
            {
                Content = JsonContent.Create(new
                {
                    text = delivery.Text,
                    attachments = attachments is null ? null : new[] { attachments }
                })
            };
            request.Headers.TryAddWithoutValidation("Authorization", delivery.Token);
            using var response = await this.CreateMaxClient().SendAsync(request, cancellationToken);

            return await ReadProviderResultAsync(response, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Uncertain("provider-timeout", "MAX не подтвердил результат отправки.");
        }
        catch (HttpRequestException)
        {
            return Uncertain("provider-connection-failed", "Не удалось подтвердить результат отправки в MAX.");
        }
    }

    private async Task<byte[]> DownloadAttachmentAsync(
        ConversationDeliveryClaimResponse delivery,
        ConversationDeliveryAttachment attachment,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/internal/conversation-deliveries/{delivery.MessageId:D}/attachments/{attachment.AttachmentId:D}");
        request.Headers.TryAddWithoutValidation("X-Notification-Service-Key", options.Value.ServiceKey);
        request.Headers.TryAddWithoutValidation("X-Conversation-Lease-Id", delivery.LeaseId.ToString("D"));
        using var response = await this.CreateOpenCRMClient().SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode
            || response.Content.Headers.ContentLength is not > 0 or > MaxAttachmentBytes
            || response.Content.Headers.ContentLength != attachment.SizeBytes)
        {
            throw new HttpRequestException($"Core API did not provide the MAX attachment (HTTP {(int)response.StatusCode}).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream((int)attachment.SizeBytes);
        var buffer = new byte[81920];
        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (output.Length + bytesRead > attachment.SizeBytes || output.Length + bytesRead > MaxAttachmentBytes)
            {
                throw new HttpRequestException("Core API returned an oversized MAX attachment.");
            }

            output.Write(buffer, 0, bytesRead);
        }

        if (output.Length != attachment.SizeBytes)
        {
            throw new HttpRequestException("Core API returned an attachment with an unexpected size.");
        }

        return output.ToArray();
    }

    private async Task<object> UploadAttachmentAsync(
        ConversationDeliveryClaimResponse delivery,
        ConversationDeliveryAttachment attachment,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var uploadType = attachment.Kind is "image" or "photo" ? "image"
            : attachment.Kind is "video" ? "video"
            : attachment.Kind is "audio" ? "audio"
            : "file";
        await rateLimiter.WaitForGlobalRequestSlotAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"uploads?type={uploadType}");
        request.Headers.TryAddWithoutValidation("Authorization", delivery.Token);
        using var response = await this.CreateMaxClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"MAX did not prepare an upload (HTTP {(int)response.StatusCode}).");
        }

        using var uploadInfo = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!uploadInfo.RootElement.TryGetProperty("url", out var uploadUrlProperty)
            || uploadUrlProperty.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(uploadUrlProperty.GetString(), UriKind.Absolute, out var uploadUrl)
            || uploadUrl.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uploadUrl.UserInfo)
            || !uploadUrl.IsDefaultPort
            || !IsTrustedUploadHost(uploadUrl.Host))
        {
            throw new InvalidOperationException("MAX returned an invalid upload descriptor.");
        }

        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(attachment.ContentType);
        multipart.Add(fileContent, "data", attachment.FileName);
        uploadRequest.Content = multipart;
        using var uploadResponse = await this.CreateUploadClient().SendAsync(uploadRequest, cancellationToken);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"MAX upload did not accept the attachment (HTTP {(int)uploadResponse.StatusCode}).");
        }

        var token = ReadUploadToken(uploadInfo.RootElement, default, uploadType);
        if (token is null)
        {
            using var uploadResult = await JsonDocument.ParseAsync(
                await uploadResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            token = ReadUploadToken(uploadInfo.RootElement, uploadResult.RootElement, uploadType);
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("MAX did not return a media token after upload.");
        }

        return new
        {
            type = uploadType,
            payload = new { token }
        };
    }

    private static string? ReadUploadToken(JsonElement uploadInfo, JsonElement uploadResult, string uploadType)
    {
        if (uploadInfo.TryGetProperty("token", out var initialToken)
            && initialToken.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(initialToken.GetString()))
        {
            return initialToken.GetString();
        }

        if (uploadResult.ValueKind == JsonValueKind.Object
            && uploadResult.TryGetProperty("token", out var uploadedToken)
            && uploadedToken.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(uploadedToken.GetString()))
        {
            return uploadedToken.GetString();
        }

        if (uploadType != "image"
            || uploadResult.ValueKind != JsonValueKind.Object
            || !uploadResult.TryGetProperty("photos", out var photos)
            || photos.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var photo in photos.EnumerateObject())
        {
            if (photo.Value.ValueKind == JsonValueKind.Object
                && photo.Value.TryGetProperty("token", out var photoToken)
                && photoToken.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(photoToken.GetString()))
            {
                return photoToken.GetString();
            }
        }

        return null;
    }

    private async Task<ConversationDeliveryResult> SaveResultOrRestoreAsync(
        Guid leaseId,
        ConversationDeliveryResult result,
        CancellationToken cancellationToken)
    {
        var saved = await stateStore.SaveResultAsync(
            leaseId,
            result.Status,
            result.ProviderMessageId,
            result.ErrorCode,
            result.ErrorMessage,
            result.SafeToRetry,
            result.RetryAfterSeconds,
            cancellationToken);
        return saved ? result : RestoreResult(await stateStore.GetAsync(leaseId, cancellationToken));
    }

    private async Task CompleteAsync(
        ConversationDeliveryClaimResponse delivery,
        ConversationDeliveryResult result,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"api/internal/conversation-deliveries/{delivery.MessageId:D}/complete")
        {
            Content = JsonContent.Create(new ConversationDeliveryCompleteRequest
            {
                LeaseId = delivery.LeaseId,
                Result = result
            })
        };
        request.Headers.TryAddWithoutValidation("X-Notification-Service-Key", options.Value.ServiceKey);
        using var response = await this.CreateOpenCRMClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Core API did not accept the MAX delivery result (HTTP {(int)response.StatusCode}).");
        }
    }

    private static async Task<ConversationDeliveryResult> ReadProviderResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        JsonDocument? document = null;
        try
        {
            document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
        }

        using (document)
        {
            var root = document?.RootElement;
            if ((int)response.StatusCode == 429)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta is { } delay
                    ? (int)Math.Ceiling(delay.TotalSeconds)
                    : 30;
                return new ConversationDeliveryResult
                {
                    Status = "failed",
                    ErrorCode = "rate-limited",
                    ErrorMessage = "MAX временно ограничил отправку.",
                    SafeToRetry = true,
                    RetryAfterSeconds = Math.Clamp(retryAfter, 1, 86400),
                    CompletedAt = DateTime.UtcNow
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                var isRejected = (int)response.StatusCode is >= 400 and < 500;
                var attachmentNotReady = root is { ValueKind: JsonValueKind.Object }
                    && root.Value.TryGetProperty("code", out var code)
                    && code.GetString() == "attachment.not.ready";
                return new ConversationDeliveryResult
                {
                    Status = isRejected ? "failed" : "uncertain",
                    ErrorCode = attachmentNotReady ? "attachment-not-ready" : $"max-http-{(int)response.StatusCode}",
                    ErrorMessage = attachmentNotReady
                        ? "MAX ещё обрабатывает вложение."
                        : isRejected ? "MAX отклонил сообщение." : "MAX не подтвердил результат отправки.",
                    SafeToRetry = attachmentNotReady,
                    RetryAfterSeconds = attachmentNotReady ? 10 : null,
                    CompletedAt = DateTime.UtcNow
                };
            }

            if (root is { ValueKind: JsonValueKind.Object }
                && root.Value.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("body", out var body)
                && body.ValueKind == JsonValueKind.Object
                && body.TryGetProperty("mid", out var id)
                && id.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(id.GetString()))
            {
                return new ConversationDeliveryResult
                {
                    Status = "accepted",
                    ProviderMessageId = id.GetString(),
                    CompletedAt = DateTime.UtcNow
                };
            }

            return Uncertain("invalid-provider-response", "MAX не вернул подтверждение сообщения.");
        }
    }

    private HttpClient CreateOpenCRMClient() => httpClientFactory.CreateClient("MaxChatOpenCRM");

    private HttpClient CreateMaxClient() => httpClientFactory.CreateClient("MaxConversationApi");

    private HttpClient CreateUploadClient() => httpClientFactory.CreateClient("MaxConversationUpload");

    private static ConversationDeliveryResult RestoreResult(Notifications.Domain.TelegramChatDeliveryState state)
    {
        int? retryAfter = state.RetryAfterAt is { } retryAt
            ? Math.Max(1, (int)Math.Ceiling((retryAt - DateTimeOffset.UtcNow).TotalSeconds))
            : null;
        return new ConversationDeliveryResult
        {
            Status = state.Status,
            ProviderMessageId = state.ProviderMessageId,
            ErrorCode = state.ErrorCode,
            ErrorMessage = state.ErrorMessage,
            SafeToRetry = state.SafeToRetry,
            RetryAfterSeconds = retryAfter,
            CompletedAt = state.UpdatedAt.UtcDateTime
        };
    }

    private static ConversationDeliveryResult Uncertain(string errorCode, string errorMessage) => new()
    {
        Status = "uncertain",
        ErrorCode = errorCode,
        ErrorMessage = errorMessage,
        CompletedAt = DateTime.UtcNow
    };

    private static ConversationDeliveryResult SafeRetry(string errorCode, string errorMessage) => new()
    {
        Status = "failed",
        ErrorCode = errorCode,
        ErrorMessage = errorMessage,
        SafeToRetry = true,
        RetryAfterSeconds = 10,
        CompletedAt = DateTime.UtcNow
    };

    private static bool IsTrustedUploadHost(string host)
    {
        return host.EndsWith(".okcdn.ru", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".oneme.ru", StringComparison.OrdinalIgnoreCase);
    }
}
