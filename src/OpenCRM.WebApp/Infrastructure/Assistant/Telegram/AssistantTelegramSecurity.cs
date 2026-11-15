using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenCRM.WebApp.Infrastructure.Assistant.Telegram;

/// <summary>Проверяет секреты вебхука и создаёт короткие подписанные ссылки Telegram callback.</summary>
public static class AssistantTelegramSecurity
{
    private const int CallbackSignatureBytes = 8;
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    /// <summary>Сравнивает секрет вебхука без раннего выхода по совпадающему префиксу.</summary>
    public static bool MatchesWebhookSecret(string? expected, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));
    }

    /// <summary>Подписывает callback для одного Telegram пользователя и задаёт ему срок действия из настроек.</summary>
    public static string CreateCallback(string secret, long telegramUserId, string command, DateTime utcNow, int lifetimeMinutes)
    {
        if (string.IsNullOrWhiteSpace(secret) || telegramUserId <= 0 || string.IsNullOrWhiteSpace(command) || lifetimeMinutes <= 0)
        {
            throw new ArgumentException("Невозможно создать Telegram callback.");
        }

        var expiresAtMinute = new DateTimeOffset(utcNow.AddMinutes(lifetimeMinutes)).ToUnixTimeSeconds() / 60;
        var payload = $"{command}:{ToBase36(expiresAtMinute)}";
        var signature = Sign(secret, telegramUserId, payload);
        var callback = $"{payload}:{signature}";
        if (Encoding.UTF8.GetByteCount(callback) > 64)
        {
            throw new InvalidOperationException("Telegram callback превышает допустимый размер.");
        }

        return callback;
    }

    /// <summary>Проверяет подпись, Telegram-пользователя и срок действия callback.</summary>
    public static bool TryValidateCallback(string secret, long telegramUserId, string callback, DateTime utcNow, int lifetimeMinutes, out string command)
    {
        command = string.Empty;
        if (telegramUserId <= 0 || lifetimeMinutes <= 0 || string.IsNullOrWhiteSpace(callback) || Encoding.UTF8.GetByteCount(callback) > 64)
        {
            return false;
        }

        var signatureSeparator = callback.LastIndexOf(':');
        if (signatureSeparator <= 0)
        {
            return false;
        }
        var payload = callback[..signatureSeparator];
        var expirySeparator = payload.LastIndexOf(':');
        if (expirySeparator <= 0 || !TryFromBase36(payload[(expirySeparator + 1)..], out var expiresAtMinute))
        {
            return false;
        }

        var nowMinute = new DateTimeOffset(utcNow).ToUnixTimeSeconds() / 60;
        if (expiresAtMinute < nowMinute || expiresAtMinute > nowMinute + lifetimeMinutes + 1)
        {
            return false;
        }

        var expected = Sign(secret, telegramUserId, payload);
        var supplied = callback[(signatureSeparator + 1)..];
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied)))
        {
            return false;
        }

        command = payload[..expirySeparator];
        return true;
    }

    /// <summary>Создаёт короткую метку состояния, связывающую кнопки с текущим списком уточнений.</summary>
    public static string HashChoice(AssistantClarification clarification)
    {
        var value = JsonSerializer.Serialize(clarification, s_json);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 3))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Sign(string secret, long telegramUserId, string payload)
    {
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{telegramUserId}:{payload}"));
        return Convert.ToBase64String(signature.AsSpan(0, CallbackSignatureBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string ToBase36(long value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        Span<char> buffer = stackalloc char[13];
        var index = buffer.Length;
        do
        {
            buffer[--index] = digits[(int)(value % 36)];
            value /= 36;
        }
        while (value > 0);

        return new string(buffer[index..]);
    }

    private static bool TryFromBase36(string value, out long result)
    {
        result = 0;
        if (value.Length is < 1 or > 13)
        {
            return false;
        }
        foreach (var character in value)
        {
            var digit = character switch
            {
                >= '0' and <= '9' => character - '0',
                >= 'a' and <= 'z' => character - 'a' + 10,
                _ => -1
            };
            if (digit < 0 || result > (long.MaxValue - digit) / 36)
            {
                return false;
            }
            result = result * 36 + digit;
        }

        return true;
    }
}
