using System.Net;
using System.Text.RegularExpressions;

namespace Notifications.Application;

/// <summary>Формирует текстовую часть письма из сохранённого HTML.</summary>
public static partial class NotificationText
{
    /// <summary> Формирует текст письма из HTML. </summary>
    public static string FromHtml(string html)
    {
        var text = HiddenContent().Replace(html, string.Empty);
        text = LineBreaks().Replace(text, Environment.NewLine);
        text = Tags().Replace(text, string.Empty);

        return WebUtility.HtmlDecode(text).Trim();
    }

    /// <summary> Регулярное выражение для удаления блоков style и script. </summary>
    [GeneratedRegex(@"<(style|script)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HiddenContent();

    /// <summary> Регулярное выражение для определения разрывов строк в HTML. </summary>
    [GeneratedRegex(@"<br\s*/?>|</(?:p|div|h[1-6]|li|tr)>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreaks();

    /// <summary> Регулярное выражение для удаления HTML-тегов. </summary>
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();
}
