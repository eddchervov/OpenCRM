using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Notifications.Contracts;

namespace Notifications.Infrastructure.Providers.UnisenderGo;

/// <summary>Проверяет подпись и переводит события Unisender в общий контракт уведомлений.</summary>
public static class UnisenderGoWebhookParser
{
    /// <summary> Проверяет подпись вебхука алгоритмом MD5. </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5351",
        Justification = "Алгоритм MD5 задан протоколом проверки вебхуков Unisender Go.")]
    public static bool VerifySignature(JsonNode root, string rawBody, string key)
    {
        var auth = Read(root, "auth");
        if (string.IsNullOrWhiteSpace(key) || auth?.Length != 32 || !auth.All(Uri.IsHexDigit))
        {
            return false;
        }

        var expected = MD5.HashData(Encoding.UTF8.GetBytes(rawBody.Replace(auth, key, StringComparison.Ordinal)));
        var actual = Convert.FromHexString(auth);

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary> Разбирает события вебхука в отчёты доставки. </summary>
    public static IReadOnlyList<NotificationDeliveryReported> Parse(JsonNode root)
    {
        var results = new List<NotificationDeliveryReported>();
        var events = ExtractEvents(root);
        foreach (var node in events)
        {
            var eventName = Read(node, "event_name");
            if (eventName == "transactional_spam_block")
            {
                // Блокировка SMTP-сервера относится к домену, а не к конкретному письму.
                continue;
            }

            var data = node["event_data"] as JsonObject ?? node["data"] as JsonObject ?? node;
            var metadata = data["metadata"] ?? data["global_metadata"];
            var key = Read(metadata, "notification_key");
            var applicationId = Read(metadata, "application_id");
            if (string.IsNullOrWhiteSpace(applicationId) || string.IsNullOrWhiteSpace(key))
            {
                // События писем без обязательной корреляции не относятся к текущему протоколу.
                continue;
            }

            var status = NormalizeStatus(Read(data, "status") ?? Read(node, "event_name"));
            if (status == null)
            {
                continue;
            }

            var time = Read(data, "event_time") ?? Read(node, "event_time");
            var occurredAt = DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedTime)
                ? parsedTime : DateTimeOffset.UtcNow;
            var recipient = Read(data, "email");
            var providerId = Read(data, "job_id") ?? Read(data, "message_id");

            var details = data["delivery_info"];
            var url = Read(data, "url") ?? Read(details, "url") ?? Read(details, "click_url");
            var ipAddress = Read(details, "ip");
            var userAgent = Read(details, "user_agent");
            var identity = JsonSerializer.Serialize(new
            {
                applicationId, key, status, time, recipient, providerId, url, ipAddress, userAgent
            });
            var eventId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
            results.Add(new NotificationDeliveryReported(eventId, applicationId, key, status, occurredAt,
                recipient, providerId, url, ipAddress, userAgent, details?.ToJsonString()));
        }

        return results;
    }

    /// <summary> Извлекает события из корня вебхука. </summary>
    private static IEnumerable<JsonNode> ExtractEvents(JsonNode root)
    {
        if (root["events_by_user"] is JsonArray users)
        {
            foreach (var user in users.OfType<JsonObject>())
            {
                if (user["events"] is not JsonArray events)
                {
                    continue;
                }

                foreach (var item in events.OfType<JsonObject>())
                {
                    yield return item;
                }
            }

            yield break;
        }

        if (root["events"] is JsonArray flatEvents)
        {
            foreach (var item in flatEvents.OfType<JsonObject>())
            {
                yield return item;
            }

            yield break;
        }

        yield return root;
    }

    /// <summary> Читает строковое поле JSON-узла. </summary>
    private static string? Read(JsonNode? node, string name)
    {
        if (node is not JsonObject obj || obj[name] is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<string>(out var text) ? text : value.ToJsonString();
    }

    /// <summary> Приводит статус провайдера к общему значению. </summary>
    private static string? NormalizeStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "accepted" or "ok_accepted" => "accepted",
            "sent" or "ok_sent" => "sent",
            "delivered" or "ok_delivered" => "delivered",
            "opened" or "read" or "ok_read" => "opened",
            "clicked" or "click" or "ok_link_visited" => "clicked",
            "unsubscribed" or "ok_unsubscribed" => "unsubscribed",
            "subscribed" or "ok_resubscribed" => "subscribed",
            "soft_bounced" or "soft_bounce" => "soft_bounced",
            "hard_bounced" or "hard_bounce" => "hard_bounced",
            "spam" or "ok_spam_folder" => "spam",
            "spam_block" or "transactional_spam_block" => "spam_block",
            { } value when value.StartsWith("err_", StringComparison.Ordinal)
                || value.StartsWith("not_sent", StringComparison.Ordinal) => "failed",
            _ => null
        };
    }
}
