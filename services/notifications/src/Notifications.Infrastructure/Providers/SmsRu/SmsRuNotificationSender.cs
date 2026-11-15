using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Application;
using Notifications.Domain;

namespace Notifications.Infrastructure.Providers.SmsRu;

/// <summary>Передаёт SMS провайдеру и проверяет принятие сообщения по его JSON-ответу.</summary>
public sealed partial class SmsRuNotificationSender(
    HttpClient httpClient,
    IOptions<DeliveryOptions> deliveryOptions,
    IOptions<SmsRuOptions> smsOptions,
    ILogger<SmsRuNotificationSender> logger) : INotificationChannelSender
{
    /// <summary>Поддерживаемый канал.</summary>
    public NotificationChannel Channel => NotificationChannel.Sms;

    /// <summary>Возвращает sms_id только после подтверждения провайдера.</summary>
    public async Task<string?> SendAsync(
        string recipient,
        string? subject,
        string body,
        bool isHtml,
        CancellationToken cancellationToken,
        string? idempotencyKey = null, string? applicationId = null)
    {
        if (deliveryOptions.Value.LogOnly)
        {
            LogSimulatedDelivery(logger);

            return $"log:{Guid.NewGuid():N}";
        }

        var options = smsOptions.Value;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("SMS.ru provider is not configured.");
        }

        // Один контракт описывает одного получателя, а не пакетную рассылку.
        var phone = recipient.Trim().TrimStart('+');
        if (phone.Length == 0 || !phone.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("A single phone number in international format is required.");
        }

        var values = new Dictionary<string, string>
        {
            ["api_id"] = options.ApiKey,
            ["to"] = phone,
            ["msg"] = body,
            ["json"] = "1"
        };
        if (!string.IsNullOrWhiteSpace(options.From))
        {
            values["from"] = options.From;
        }

        using var content = new FormUrlEncodedContent(values);
        using var response = await httpClient.PostAsync("/sms/send", content, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;

        if (!IsAccepted(root)
            || !root.TryGetProperty("sms", out var messages)
            || messages.ValueKind != JsonValueKind.Object
            || !messages.TryGetProperty(phone, out var message)
            || !IsAccepted(message)
            || !message.TryGetProperty("sms_id", out var messageId)
            || messageId.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(messageId.GetString()))
        {
            throw new InvalidOperationException("SMS.ru did not confirm acceptance of the message.");
        }

        return messageId.GetString();
    }

    /// <summary> Проверяет принятие SMS по JSON-ответу провайдера. </summary>
    private static bool IsAccepted(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("status", out var status)
            && status.ValueKind == JsonValueKind.String
            && status.GetString() == "OK"
            && value.TryGetProperty("status_code", out var code)
            && code.ValueKind == JsonValueKind.Number
            && code.TryGetInt32(out var number)
            && number == 100;
    }

    /// <summary> Логирует имитацию отправки SMS в режиме LogOnly. </summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Отправка SMS имитирована в режиме LogOnly.")]
    private static partial void LogSimulatedDelivery(ILogger logger);
}
