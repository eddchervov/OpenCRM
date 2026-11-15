using System.Security.Cryptography;
using System.Text;

namespace OpenCRM.Modules.Notifications.Emails;

/// <summary> Утилиты создания и проверки токенов отписки. </summary>
public static class EmailUnsubscribeToken
{
    /// <summary> Создаёт токен отписки для пользователя и адреса email. </summary>
    public static string Create(string userId, string email, string secret)
    {
        var signature = Sign(userId, email, secret);
        return ToBase64Url(signature);
    }

    /// <summary> Проверяет токен отписки для пользователя и адреса email. </summary>
    public static bool Validate(string userId, string email, string token, string secret)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var expected = Sign(userId, email, secret);
            var actual = FromBase64Url(token);
            return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary> Вычисляет подпись токена отписки по секрету. </summary>
    private static byte[] Sign(string userId, string email, string secret)
    {
        var normalized = $"{userId.Trim()}|{email.Trim().ToUpperInvariant()}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized));
    }

    /// <summary> Кодирует массив байтов в Base64Url. </summary>
    private static string ToBase64Url(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary> Декодирует строку Base64Url в массив байтов. </summary>
    private static byte[] FromBase64Url(string value)
    {
        var normalized = value.Trim().Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        return Convert.FromBase64String(normalized);
    }
}
