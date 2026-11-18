using System.Globalization;

namespace OpenCRM.Admin.Api;

/// <summary> Язык интерфейса настольного клиента и его тег для выбора переводов на сервере. </summary>
public sealed record AdminUiLanguageOption(string Code, string NativeName, string LanguageTag, IReadOnlyList<string> Aliases);

public static class AdminUiLanguage
{
    /// <summary> Поддерживаемые коды, самоназвания, культуры и языковые алиасы. </summary>
    public static IReadOnlyList<AdminUiLanguageOption> Supported { get; } =
    [
        new("ru", "Русский", "ru-RU", ["ru"]),
        new("en", "English", "en-US", ["en"]),
        new("zh-Hans", "简体中文", "zh-CN", ["zh-Hans", "zh-CN", "zh-SG"])
    ];

    /// <summary> Возвращает поддерживаемый код или русский язык для неизвестного значения. </summary>
    public static string Normalize(string? language) => AdminUiLanguage.Supported
        .FirstOrDefault(option => option.Code == language)?.Code ?? "ru";

    /// <summary> Возвращает языковой тег в заголовке Accept-Language. </summary>
    public static string GetAcceptLanguage(string language) => CultureInfo
        .GetCultureInfo(AdminUiLanguage.Supported.First(option => option.Code == AdminUiLanguage.Normalize(language)).LanguageTag)
        .Name;
}
