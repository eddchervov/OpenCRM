using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Notifications.Contracts;

namespace Notifications.Infrastructure;

/// <summary>Передаёт одну захваченную core API команду Telegram, сохраняя идемпотентное состояние.</summary>
public sealed class TelegramChatDeliveryProcessor(
    IHttpClientFactory httpClientFactory,
    ConversationDeliveryAttemptStore stateStore,
    IOptions<TelegramChatOptions> options) : IConversationDeliveryProcessor
{
    private const long MaxAttachmentBytes = 20 * 1024 * 1024;

    /// <summary>Захватывает сообщение, выполняет не более одного внешнего вызова и подтверждает его исход.</summary>
    public ConversationDeliveryProvider ProviderCode => ConversationDeliveryProvider.Telegram;

    /// <inheritdoc />
    public async Task ProcessAsync(ConversationDeliveryWakeup command, CancellationToken cancellationToken)
    {
        if (command.ProviderCode != this.ProviderCode)
        {
            throw new InvalidOperationException("Telegram delivery processor received a different provider.");
        }

        var delivery = await this.ClaimAsync(command, cancellationToken);
        if (delivery == null)
        {
            return;
        }

        if (!IsValidDelivery(command, delivery))
        {
            throw new InvalidOperationException("Core API returned an invalid Telegram delivery claim.");
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
            result = await this.SaveResultOrRestoreAsync(
                delivery.LeaseId,
                result,
                cancellationToken);
        }
        else if (attempt.Status == "started")
        {
            var providerDeadline = attempt.StartedAt?.AddSeconds(options.Value.RequestTimeoutSeconds + 5);
            if (providerDeadline is null || providerDeadline > DateTimeOffset.UtcNow)
            {
                throw new ConversationDeliveryDeferredException(
                    "Предыдущая попытка отправки ещё может завершиться.");
            }

            result = new ConversationDeliveryResult
            {
                Status = "uncertain",
                ErrorCode = "provider-result-unknown",
                ErrorMessage = "Telegram не подтвердил результат предыдущей попытки.",
                CompletedAt = DateTime.UtcNow
            };
            result = await this.SaveResultOrRestoreAsync(
                delivery.LeaseId,
                result,
                cancellationToken);
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
        if (saved)
        {
            return result;
        }

        var persistedResult = await stateStore.GetAsync(leaseId, cancellationToken);

        return RestoreResult(persistedResult);
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
            throw new ConversationDeliveryDeferredException(
                "Доставка уже захвачена другим обработчиком.",
                retryAfter);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Core API did not provide Telegram delivery (HTTP {(int)response.StatusCode}).");
        }

        return await response.Content.ReadFromJsonAsync<ConversationDeliveryClaimResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Core API returned an empty Telegram delivery claim.");
    }

    private static bool IsValidDelivery(
        ConversationDeliveryWakeup command,
        ConversationDeliveryClaimResponse delivery)
    {
        return delivery.MessageId == command.MessageId
            && delivery.RequestId == command.RequestId
            && delivery.LeaseId != Guid.Empty
            && delivery.OrganizationId != Guid.Empty
            && delivery.ProviderCode == ConversationDeliveryProvider.Telegram
            && delivery.ConnectionId != Guid.Empty
            && long.TryParse(delivery.ProviderBotId, NumberStyles.None, CultureInfo.InvariantCulture, out var botId)
            && botId > 0
            && long.TryParse(delivery.DeliveryAddress, NumberStyles.None, CultureInfo.InvariantCulture, out var chatId)
            && chatId > 0
            && IsValidBotToken(delivery.Token)
            && delivery.Attachments.Count <= 1
            && (delivery.Text?.Length ?? 0) <= 4096
            && delivery.Attachments.All(attachment => attachment.SizeBytes > 0
                && attachment.SizeBytes <= MaxAttachmentBytes
                && attachment.AttachmentId != Guid.Empty
                && (attachment.Kind is "photo" or "document")
                && !string.IsNullOrWhiteSpace(attachment.FileName)
                && attachment.FileName.Length <= 255
                && Path.GetFileName(attachment.FileName) == attachment.FileName
                && !attachment.FileName.Contains('/')
                && !attachment.FileName.Contains('\\')
                && !attachment.FileName.Any(char.IsControl)
                && !string.IsNullOrWhiteSpace(attachment.ContentType)
                && MediaTypeHeaderValue.TryParse(attachment.ContentType, out _));
    }

    private async Task<ConversationDeliveryResult> SendAsync(
        ConversationDeliveryClaimResponse delivery,
        CancellationToken cancellationToken)
    {
        HttpRequestMessage request;
        try
        {
            request = delivery.Attachments.Count == 0
                ? this.CreateTextRequest(delivery)
                : await this.CreateMediaRequestAsync(delivery, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return new ConversationDeliveryResult
            {
                Status = "failed",
                ErrorCode = "attachment-unavailable",
                ErrorMessage = "Не удалось получить вложение для отправки.",
                SafeToRetry = true,
                RetryAfterSeconds = 10,
                CompletedAt = DateTime.UtcNow
            };
        }
        catch (InvalidOperationException exception)
        {
            return new ConversationDeliveryResult
            {
                Status = "failed",
                ErrorCode = "invalid-message-content",
                ErrorMessage = exception.Message,
                CompletedAt = DateTime.UtcNow
            };
        }

        try
        {
            using (request)
            {
                if (!await stateStore.TryMarkProviderStartedAsync(delivery.LeaseId, cancellationToken))
                {
                    throw new ConversationDeliveryDeferredException(
                        "Другой обработчик уже начал отправку этого сообщения.");
                }

                using var response = await this.CreateRelayClient().SendAsync(request, cancellationToken);
                return await ReadProviderResultAsync(response, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Uncertain("provider-timeout", "Telegram не подтвердил результат отправки.");
        }
        catch (HttpRequestException)
        {
            return Uncertain("provider-connection-failed", "Не удалось подтвердить результат отправки в Telegram.");
        }
    }

    private HttpRequestMessage CreateTextRequest(ConversationDeliveryClaimResponse delivery)
    {
        if (string.IsNullOrWhiteSpace(delivery.Text))
        {
            throw new InvalidOperationException("Telegram message must contain text or one attachment.");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "sendOrganizationMessage")
        {
            Content = JsonContent.Create(new
            {
                bot_token = delivery.Token,
                chat_id = delivery.DeliveryAddress,
                text = delivery.Text
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.RelayKey);

        return request;
    }

    private async Task<HttpRequestMessage> CreateMediaRequestAsync(
        ConversationDeliveryClaimResponse delivery,
        CancellationToken cancellationToken)
    {
        var attachment = delivery.Attachments[0];
        var isPhoto = attachment.Kind == "photo";
        if (isPhoto && (attachment.SizeBytes > 10 * 1024 * 1024
            || attachment.ContentType is not ("image/jpeg" or "image/png" or "image/webp")))
        {
            throw new InvalidOperationException("Telegram photo attachments must be a supported image up to 10 MiB.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/internal/conversation-deliveries/{delivery.MessageId:D}/attachments/{attachment.AttachmentId:D}");
        request.Headers.TryAddWithoutValidation("X-Notification-Service-Key", options.Value.ServiceKey);
        request.Headers.TryAddWithoutValidation("X-Conversation-Lease-Id", delivery.LeaseId.ToString("D"));
        using var response = await this.CreateOpenCRMClient().SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Core API did not provide the Telegram attachment (HTTP {(int)response.StatusCode}).");
        }

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is null or <= 0 or > MaxAttachmentBytes)
        {
            throw new HttpRequestException("Core API returned an invalid Telegram attachment size.");
        }

        await using var attachmentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var bytes = await ReadBoundedAsync(attachmentStream, MaxAttachmentBytes, cancellationToken);
        if (bytes.LongLength != attachment.SizeBytes || bytes.LongLength > MaxAttachmentBytes)
        {
            throw new HttpRequestException("Core API returned a Telegram attachment with an unexpected size.");
        }

        if (!isPhoto && !string.IsNullOrWhiteSpace(delivery.Text) && delivery.Text.Length > 1024)
        {
            throw new InvalidOperationException("Telegram document captions cannot exceed 1024 characters.");
        }

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(delivery.Token), "bot_token");
        content.Add(new StringContent(delivery.DeliveryAddress), "chat_id");
        if (!string.IsNullOrWhiteSpace(delivery.Text))
        {
            content.Add(new StringContent(delivery.Text), "caption");
        }

        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(attachment.ContentType);
        content.Add(fileContent, "file", Path.GetFileName(attachment.FileName));
        var mediaRequest = new HttpRequestMessage(
            HttpMethod.Post,
            isPhoto ? "sendOrganizationPhoto" : "sendOrganizationDocument")
        {
            Content = content
        };
        mediaRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.RelayKey);

        return mediaRequest;
    }

    private static async Task<ConversationDeliveryResult> ReadProviderResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        JsonDocument? document = null;
        try
        {
            document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
        }

        using (document)
        {
            var root = document?.RootElement;
            if (response.StatusCode == (HttpStatusCode)429)
            {
                var retryAfter = root.HasValue
                    && root.Value.TryGetProperty("parameters", out var parameters)
                    && parameters.TryGetProperty("retry_after", out var retryProperty)
                    && retryProperty.TryGetInt32(out var seconds)
                    ? Math.Clamp(seconds, 1, 86400)
                    : 30;
                return new ConversationDeliveryResult
                {
                    Status = "failed",
                    ErrorCode = "rate-limited",
                    ErrorMessage = "Telegram временно ограничил отправку.",
                    SafeToRetry = true,
                    RetryAfterSeconds = retryAfter,
                    CompletedAt = DateTime.UtcNow
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                var isRejected = (int)response.StatusCode is >= 400 and < 500;
                return new ConversationDeliveryResult
                {
                    Status = isRejected ? "failed" : "uncertain",
                    ErrorCode = $"telegram-http-{(int)response.StatusCode}",
                    ErrorMessage = isRejected
                        ? "Telegram отклонил сообщение. Проверьте подключение бота и получателя."
                        : "Telegram не подтвердил результат отправки.",
                    CompletedAt = DateTime.UtcNow
                };
            }

            if (root is { ValueKind: JsonValueKind.Object }
                && root.Value.TryGetProperty("ok", out var accepted)
                && accepted.ValueKind == JsonValueKind.True
                && root.Value.TryGetProperty("result", out var result)
                && result.TryGetProperty("message_id", out var messageId)
                && messageId.TryGetInt64(out var providerId)
                && providerId > 0)
            {
                return new ConversationDeliveryResult
                {
                    Status = "accepted",
                    ProviderMessageId = providerId.ToString(CultureInfo.InvariantCulture),
                    CompletedAt = DateTime.UtcNow
                };
            }

            return Uncertain("invalid-provider-response", "Telegram не вернул подтверждение сообщения.");
        }
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
            throw new HttpRequestException(
                $"Core API did not accept the Telegram delivery result (HTTP {(int)response.StatusCode}).");
        }
    }

    private static ConversationDeliveryResult RestoreResult(Notifications.Domain.TelegramChatDeliveryState state)
    {
        int? retryAfter = state.RetryAfterAt is { } retryAt
            ? Math.Max(1, (int)Math.Ceiling((retryAt - DateTimeOffset.UtcNow).TotalSeconds))
            : null;

        return new ConversationDeliveryResult
        {
            Status = state.Status == "rate-limited" ? "failed" : state.Status,
            ProviderMessageId = state.ProviderMessageId,
            ErrorCode = state.ErrorCode,
            ErrorMessage = state.ErrorMessage,
            SafeToRetry = state.SafeToRetry,
            RetryAfterSeconds = retryAfter,
            CompletedAt = state.UpdatedAt.UtcDateTime
        };
    }

    private HttpClient CreateOpenCRMClient()
    {
        return httpClientFactory.CreateClient("TelegramChatOpenCRM");
    }

    private HttpClient CreateRelayClient()
    {
        return httpClientFactory.CreateClient("TelegramChatRelay");
    }

    private static ConversationDeliveryResult Uncertain(string errorCode, string errorMessage)
    {
        return new ConversationDeliveryResult
        {
            Status = "uncertain",
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            CompletedAt = DateTime.UtcNow
        };
    }

    private static bool IsValidBotToken(string token)
    {
        var separatorIndex = token.IndexOf(':');
        if (separatorIndex is < 5 or > 15 || token.Length - separatorIndex - 1 is < 20 or > 100)
        {
            return false;
        }

        return token.AsSpan(0, separatorIndex).ToString().All(char.IsAsciiDigit)
            && token.AsSpan(separatorIndex + 1).ToString().All(character => char.IsAsciiLetterOrDigit(character)
                || character is '_' or '-');
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream source,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var destination = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (destination.Length + bytesRead > maxBytes)
            {
                throw new HttpRequestException("The Telegram attachment exceeds the configured size limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return destination.ToArray();
    }
}

