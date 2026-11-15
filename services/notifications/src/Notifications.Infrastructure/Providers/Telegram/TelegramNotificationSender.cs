using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Notifications.Application;
using Notifications.Domain;

namespace Notifications.Infrastructure.Providers.Telegram;

/// <summary>Отправляет админские или клиентские сообщения через Telegram.</summary>
public sealed partial class TelegramNotificationSender(
    HttpClient httpClient,
    DeliveryOptions deliveryOptions,
    TelegramOptions telegramOptions,
    ILogger<TelegramNotificationSender> logger,
    NotificationChannel channel) : INotificationChannelSender
{
    /// <summary>Поддерживаемый канал.</summary>
    public NotificationChannel Channel => channel;

    /// <summary>Передаёт сообщение разрешённому получателю и возвращает подтверждённый идентификатор.</summary>
    public async Task<string?> SendAsync(
        string recipient,
        string? subject,
        string body,
        bool isHtml,
        CancellationToken cancellationToken,
        string? idempotencyKey = null,
        string? applicationId = null)
    {
        var isOwner = channel == NotificationChannel.Telegram
            && string.Equals(recipient, "owner", StringComparison.Ordinal);
        var isOrganization = channel == NotificationChannel.TelegramClient
            && TryParseOrganizationRecipient(recipient, out _, out _);
        if (!isOwner && !isOrganization)
        {
            throw new ArgumentException(
                "Telegram recipient must be 'owner' or '<organizationGuidN>:<chatId>'.",
                nameof(recipient));
        }

        if (deliveryOptions.LogOnly)
        {
            LogSimulatedDelivery(logger);

            return $"log:{Guid.NewGuid():N}";
        }

        var options = telegramOptions;
        var useRelay = !string.IsNullOrWhiteSpace(options.RelayUrl);
        var messageText = CreateMessageText(subject, body, isHtml);
        if (isOrganization)
        {
            if (!TryParseOrganizationRecipient(recipient, out var organizationId, out var chatId)
                || string.IsNullOrWhiteSpace(options.CredentialBaseUrl)
                || string.IsNullOrWhiteSpace(options.CredentialKey)
                || string.IsNullOrWhiteSpace(options.RelayUrl))
            {
                throw new InvalidOperationException("Telegram delivery for organizations is not configured.");
            }

            var botToken = await ResolveOrganizationBotTokenAsync(
                organizationId,
                chatId,
                idempotencyKey,
                options,
                cancellationToken);

            return await SendViaRelayAsync(
                options.RelayUrl,
                options.RelayKey,
                new { bot_token = botToken, chat_id = chatId, text = messageText },
                cancellationToken);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            useRelay ? options.RelayUrl : $"/bot{options.BotToken}/sendMessage")
        {
            Content = useRelay
                ? JsonContent.Create(new { text = messageText })
                : JsonContent.Create(new
                {
                    chat_id = options.OwnerChatId,
                    text = messageText
                })
        };
        if (useRelay)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.RelayKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            LogNetworkFailure(logger, useRelay ? "relay" : "direct", exception.HttpRequestError);
            throw new InvalidOperationException("Telegram did not confirm acceptance of the message.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimeout(logger, useRelay ? "relay" : "direct");
            throw new InvalidOperationException("Telegram did not confirm acceptance of the message.");
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                LogHttpFailure(logger, useRelay ? "relay" : "direct", (int)response.StatusCode);
                throw new InvalidOperationException("Telegram did not confirm acceptance of the message.");
            }

            return await ReadAcceptedMessageIdAsync(response, cancellationToken);
        }
    }

    /// <summary> Определяет токен бота организации для Telegram. </summary>
    private async Task<string?> ResolveOrganizationBotTokenAsync(
        Guid organizationId,
        string chatId,
        string? notificationKey,
        TelegramOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notificationKey))
        {
            throw new ArgumentException("Telegram organization delivery requires a notification key.", nameof(notificationKey));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{options.CredentialBaseUrl.TrimEnd('/')}/api/internal/notification-bots/telegram/{organizationId:N}");
        request.Headers.TryAddWithoutValidation("X-Notification-Service-Key", options.CredentialKey);
        request.Headers.TryAddWithoutValidation("X-Notification-Key", notificationKey);
        request.Headers.TryAddWithoutValidation("X-Notification-Recipient", chatId);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"The organization Telegram bot token could not be resolved (HTTP {(int)response.StatusCode}).");
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
                return token.GetString();
            }
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The organization Telegram bot token could not be resolved.");
        }

        throw new InvalidOperationException("The organization Telegram bot token could not be resolved.");
    }

    /// <summary> Отправляет сообщение через relay-сервис Telegram. </summary>
    private async Task<string?> SendViaRelayAsync(
        string relayUrl,
        string relayKey,
        object payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relayUrl)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", relayKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            LogHttpFailure(logger, "relay", (int)response.StatusCode);
            throw new InvalidOperationException("Telegram did not confirm acceptance of the message.");
        }

        return await ReadAcceptedMessageIdAsync(response, cancellationToken);
    }

    /// <summary> Читает идентификатор принятого сообщения из ответа. </summary>
    private static async Task<string?> ReadAcceptedMessageIdAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("ok", out var accepted)
                && accepted.ValueKind == JsonValueKind.True
                && root.TryGetProperty("result", out var result)
                && result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("message_id", out var messageId)
                && messageId.ValueKind == JsonValueKind.Number
                && messageId.TryGetInt64(out var id)
                && id > 0)
            {
                return id.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Telegram did not confirm acceptance of the message.");
        }

        throw new InvalidOperationException("Telegram did not confirm acceptance of the message.");
    }

    /// <summary> Разбирает получателя-организацию Telegram. </summary>
    private static bool TryParseOrganizationRecipient(string recipient, out Guid organizationId, out string chatId)
    {
        organizationId = Guid.Empty;
        chatId = string.Empty;
        var separatorIndex = recipient.IndexOf(':');
        if (separatorIndex != 32
            || !Guid.TryParseExact(recipient.AsSpan(0, separatorIndex), "N", out organizationId))
        {
            return false;
        }

        chatId = recipient[(separatorIndex + 1)..];
        return long.TryParse(chatId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsedChatId)
            && parsedChatId != 0
            && string.Equals(chatId, parsedChatId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary> Формирует текст сообщения из темы и тела. </summary>
    private static string CreateMessageText(string? subject, string body, bool isHtml)
    {
        var text = isHtml ? NotificationText.FromHtml(body) : body;

        return string.IsNullOrWhiteSpace(subject) ? text : $"{subject}\n\n{text}";
    }

    /// <summary> Логирует имитацию отправки в Telegram в режиме LogOnly. </summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Отправка сообщения в Telegram имитирована в режиме LogOnly.")]
    private static partial void LogSimulatedDelivery(ILogger logger);

    /// <summary> Логирует ошибку соединения с Telegram. </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram: ошибка соединения, маршрут {Route}, тип {Error}.")]
    private static partial void LogNetworkFailure(ILogger logger, string route, HttpRequestError error);

    /// <summary> Логирует превышение времени ожидания Telegram. </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram: превышено время ожидания, маршрут {Route}.")]
    private static partial void LogTimeout(ILogger logger, string route);

    /// <summary> Логирует HTTP-ошибку при обращении к Telegram. </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Telegram: HTTP {StatusCode}, маршрут {Route}.")]
    private static partial void LogHttpFailure(ILogger logger, string route, int statusCode);
}
