namespace OpenCRM.WebApp.Infrastructure.Localization;

/// <summary> Описание языка интерфейса и допустимых имён его культуры. </summary>
public sealed record UiLanguageDefinition(
    string Code,
    string NativeName,
    string CultureName,
    string FormattingCultureName,
    IReadOnlyList<string> Aliases);
