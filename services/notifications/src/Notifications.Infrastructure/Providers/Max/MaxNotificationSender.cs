using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Application;
using Notifications.Domain;

namespace Notifications.Infrastructure.Providers.Max;

/// <summary>Передаёт сообщения пользователям MAX с токеном бота нужной организации.</summary>
public sealed partial class MaxNotificationSender(
    HttpClient httpClient,
    IMaxConversationRateLimiter rateLimiter,
    IOptions<DeliveryOptions> deliveryOptions,
    IOptions<MaxOptions> maxOptions,
    ILogger<MaxNotificationSender> logger) : INotificationChannelSender
{
    /// <summary> Базовый URL провайдера MAX. </summary>
    private const string ProviderBaseUrl = "https://platform-api2.max.ru";

    /// <summary>Поддерживаемый канал.</summary>
    public NotificationChannel Channel => NotificationChannel.Max;

    /// <summary>Передаёт сообщение указанному пользователю и возвращает подтверждённый идентификатор.</summary>
    public async Task<string?> SendAsync(
        string recipient,
        string? subject,
        string body,
        bool isHtml,
        CancellationToken cancellationToken,
        string? idempotencyKey = null,
        string? applicationId = null)
    {
        if (!TryParseRecipient(recipient, out var organizationId, out var maxUserId))
        {
            throw new ArgumentException("MAX recipient must be '<organizationGuidN>:<maxUserId>'.", nameof(recipient));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("MAX delivery requires a notification key.", nameof(idempotencyKey));
        }

        if (deliveryOptions.Value.LogOnly)
        {
            LogSimulatedDelivery(logger);

            return $"log:{Guid.NewGuid():N}";
        }

        var options = maxOptions.Value;
        var botToken = await ResolveBotTokenAsync(organizationId, idempotencyKey, options, cancellationToken);
        var messageText = CreateMessageText(subject, body, isHtml);
        await using var chatRateLease = await rateLimiter.AcquireConversationAsync(
            $"{organizationId:N}:{maxUserId}",
            cancellationToken);
        await rateLimiter.WaitForGlobalRequestSlotAsync(cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{ProviderBaseUrl}/messages?user_id={Uri.EscapeDataString(maxUserId)}")
        {
            Content = JsonContent.Create(new { text = messageText })
        };
        request.Headers.TryAddWithoutValidation("Authorization", botToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"MAX did not confirm acceptance of the message (HTTP {(int)response.StatusCode}).");
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("body", out var messageBody)
                && messageBody.ValueKind == JsonValueKind.Object
                && messageBody.TryGetProperty("mid", out var messageId)
                && messageId.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(messageId.GetString()))
            {
                return messageId.GetString();
            }
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("MAX did not return a message identifier.");
        }

        throw new InvalidOperationException("MAX did not return a message identifier.");
    }

    /// <summary> Разбирает получателя уведомления MAX. </summary>
    private static bool TryParseRecipient(string recipient, out Guid organizationId, out string maxUserId)
    {
        organizationId = Guid.Empty;
        maxUserId = string.Empty;
        var separatorIndex = recipient.IndexOf(':');
        if (separatorIndex != 32
            || !Guid.TryParseExact(recipient.AsSpan(0, separatorIndex), "N", out organizationId))
        {
            return false;
        }

        maxUserId = recipient[(separatorIndex + 1)..];
        return long.TryParse(maxUserId, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
            && userId > 0;
    }

    /// <summary> Определяет токен бота организации для отправки. </summary>
    private async Task<string> ResolveBotTokenAsync(
        Guid organizationId,
        string? notificationKey,
        MaxOptions options,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{options.CredentialBaseUrl.TrimEnd('/')}/api/internal/notification-bots/max/{organizationId:N}");
        request.Headers.TryAddWithoutValidation("X-Notification-Service-Key", options.CredentialKey);
        request.Headers.TryAddWithoutValidation("X-Notification-Key", notificationKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"The organization MAX bot token could not be resolved (HTTP {(int)response.StatusCode}).");
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("botToken", out var token)
                && token.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(token.GetString()))
            {
                return token.GetString()!;
            }
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The organization MAX bot token could not be resolved.");
        }

        throw new InvalidOperationException("The organization MAX bot token could not be resolved.");
    }

    /// <summary> Формирует текст сообщения из темы и тела. </summary>
    private static string CreateMessageText(string? subject, string body, bool isHtml)
    {
        var text = isHtml ? NotificationText.FromHtml(body) : body;

        return string.IsNullOrWhiteSpace(subject) ? text : $"{subject}\n\n{text}";
    }

    /// <summary> Логирует имитацию отправки в режиме LogOnly. </summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Отправка сообщения в MAX имитирована в режиме LogOnly.")]
    private static partial void LogSimulatedDelivery(ILogger logger);
}
