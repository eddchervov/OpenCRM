using System.Net;
using System.Text.RegularExpressions;
using Notifications.Application;

namespace Notifications.Infrastructure;

/// <summary>Подставляет текст и HTTP(S)-ссылки, не исполняя выражения и HTML из параметров.</summary>
public sealed partial class TemplateRenderer : ITemplateRenderer
{
    /// <summary>Отклоняет незакрытые переменные и неподдерживаемые выражения.</summary>
    public void Validate(string templateText)
    {
        ArgumentNullException.ThrowIfNull(templateText);
        var remainder = TokenRegex().Replace(templateText, string.Empty);
        if (remainder.Contains("{{", StringComparison.Ordinal) || remainder.Contains("}}", StringComparison.Ordinal))
        {
            throw new ArgumentException("Template contains an invalid placeholder.");
        }
    }

    /// <summary>Проверяет обязательные параметры и экранирует значения в HTML.</summary>
    public string Render(
        string templateText,
        IReadOnlyDictionary<string, string> data,
        bool htmlEncodeValues)
    {
        this.Validate(templateText);

        return TokenRegex().Replace(templateText, match =>
        {
            var key = match.Groups["key"].Value;
            if (!data.TryGetValue(key, out var value))
            {
                throw new ArgumentException($"Template variable '{key}' was not provided.");
            }

            if (match.Groups["url"].Success && !IsHttpUrl(value))
            {
                throw new ArgumentException($"Template variable '{key}' must be an absolute HTTP(S) URL.");
            }

            // Значения не проходят повторную подстановку и не могут добавлять HTML-разметку.
            return htmlEncodeValues ? WebUtility.HtmlEncode(value) : value;
        });
    }

    /// <summary> Проверяет, что значение является HTTP(S)-ссылкой. </summary>
    private static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && !string.IsNullOrEmpty(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo)
            && !value.Any(char.IsControl);
    }

    /// <summary> Регулярное выражение для поиска подстановочных токенов шаблона. </summary>
    [GeneratedRegex(@"(?<!{){{\s*(?<url>url:)?(?<key>[a-zA-Z0-9_.-]+)\s*}}(?!})", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
