using System.Globalization;

namespace OpenCRM.WebApp.Infrastructure.Localization;

public static class SupportedUiLanguages
{
    public const string CookieName = ".AspNetCore.Culture";
    public const string DefaultCultureName = "en-US";
    public const string RequestCultureItemKey = "OpenCRM.PreferredUiCulture";
    public const string PersonalCurrencyItemKey = "OpenCRM.PersonalCurrency";
    public const string RequestLanguageHeader = "X-OpenCRM-UI-Language";

    public static IReadOnlyList<UiLanguageDefinition> Languages { get; } =
    [
        new("ru", "Русский", "ru-RU", "ru-RU", []),
        new("en", "English", "en-US", "en-US", ["en-GB"]),
        new("zh-Hans", "简体中文", "zh-Hans", "zh-CN", ["zh", "zh-CN", "zh-SG"])
    ];

    private static readonly IReadOnlyDictionary<string, string> s_cultureNames = Languages
        .SelectMany(language => language.Aliases.Append(language.Code).Append(language.CultureName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(alias => new KeyValuePair<string, string>(alias, language.CultureName)))
        .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> s_formattingCultureNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ru-RU"] = "ru-RU",
            ["en-US"] = "en-US",
            ["en-GB"] = "en-GB",
            ["zh-CN"] = "zh-CN"
        };

    public static IReadOnlyList<CultureInfo> Cultures { get; } = Languages
        .Select(language => CultureInfo.GetCultureInfo(language.CultureName))
        .ToArray();

    public static IReadOnlyList<CultureInfo> FormattingCultures { get; } =
    [
        CultureInfo.GetCultureInfo("ru-RU"),
        CultureInfo.GetCultureInfo("en-US"),
        CultureInfo.GetCultureInfo("en-GB"),
        CultureInfo.GetCultureInfo("zh-CN")
    ];

    public static bool TryResolveCulture(string? value, out CultureInfo culture)
    {
        culture = CultureInfo.GetCultureInfo(DefaultCultureName);
        if (string.IsNullOrWhiteSpace(value) || !s_cultureNames.TryGetValue(value.Trim(), out var cultureName))
        {
            return false;
        }

        culture = CultureInfo.GetCultureInfo(cultureName);

        return true;
    }

    public static bool TryResolveLanguage(string? value, out string language)
    {
        language = string.Empty;
        if (!TryResolveCulture(value, out var culture))
        {
            return false;
        }

        var cultureName = culture.Name;
        language = Languages.Single(item => item.CultureName == cultureName).Code;

        return language.Length > 0;
    }

    public static bool TryResolveFormattingCulture(string? value, out CultureInfo culture)
    {
        culture = CultureInfo.GetCultureInfo(DefaultCultureName);
        if (string.IsNullOrWhiteSpace(value) || !s_formattingCultureNames.TryGetValue(value.Trim(), out var cultureName))
        {
            return false;
        }

        culture = CultureInfo.GetCultureInfo(cultureName);

        return true;
    }

    public static CultureInfo GetDefaultFormattingCulture(CultureInfo uiCulture)
    {
        var language = Languages.FirstOrDefault(item => item.CultureName == uiCulture.Name);

        return CultureInfo.GetCultureInfo(language?.FormattingCultureName ?? DefaultCultureName);
    }

    public static string ResolveLanguage(string? cultureName)
    {
        return TryResolveLanguage(cultureName, out var language) ? language : "en";
    }
}
