using System.Globalization;
using System.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using OpenCRM.WebApp.Infrastructure.Api;
using OpenCRM.WebApp.Infrastructure.Localization;

namespace OpenCRM.Hosting.Tests.Unit;

public sealed class SupportedUiLanguagesTests
{
    [Fact]
    public void RegistryDefinesSelectorsAliasesAndDefaultFormatsTogether()
    {
        Assert.Equal(SupportedUiLanguages.Languages.Count, SupportedUiLanguages.Cultures.Count);
        foreach (var language in SupportedUiLanguages.Languages)
        {
            Assert.False(string.IsNullOrWhiteSpace(language.NativeName));
            foreach (var alias in language.Aliases.Append(language.Code).Append(language.CultureName))
            {
                Assert.True(SupportedUiLanguages.TryResolveLanguage(alias, out var resolved));
                Assert.Equal(language.Code, resolved);
            }

            var culture = CultureInfo.GetCultureInfo(language.CultureName);
            var format = SupportedUiLanguages.GetDefaultFormattingCulture(culture);

            Assert.Equal(language.FormattingCultureName, format.Name);
            Assert.Contains(format, SupportedUiLanguages.FormattingCultures);
        }
    }

    [Theory]
    [InlineData("ru-RU", "ru-RU", "ru")]
    [InlineData("en-US", "en-US", "en")]
    [InlineData("zh-CN", "zh-Hans", "zh-Hans")]
    [InlineData("zh-Hans", "zh-Hans", "zh-Hans")]
    public void TryResolveCultureMapsSupportedLocale(string locale, string expectedCulture, string expectedLanguage)
    {
        var isSupported = SupportedUiLanguages.TryResolveCulture(locale, out var culture);
        var hasLanguage = SupportedUiLanguages.TryResolveLanguage(locale, out var language);

        Assert.True(isSupported);
        Assert.True(hasLanguage);
        Assert.Equal(expectedCulture, culture.Name);
        Assert.Equal(expectedLanguage, language);
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("zh-Hant")]
    [InlineData("")]
    public void TryResolveCultureRejectsUnsupportedLocale(string locale)
    {
        Assert.False(SupportedUiLanguages.TryResolveCulture(locale, out _));
    }

    [Fact]
    public async Task ProviderUsesSupportedBrowserLanguageAndSkipsUnsupportedLanguages()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = "fr-FR, zh-CN;q=0.8, en-US;q=0.7";

        var result = await new UiRequestCultureProvider().DetermineProviderCultureResult(context);

        Assert.NotNull(result);
        Assert.Equal("zh-CN", result.Cultures.First().Value);
        Assert.Equal("zh-Hans", result.UICultures.First().Value);
    }

    [Theory]
    [InlineData("ru", "ru-RU", "ru-RU")]
    [InlineData("en", "en-US", "en-US")]
    [InlineData("zh-Hans", "zh-CN", "zh-Hans")]
    public async Task ProviderSupportsGenericBrowserLanguages(string browserLanguage, string expectedFormat, string expectedUi)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = browserLanguage;

        var result = await new UiRequestCultureProvider().DetermineProviderCultureResult(context);

        Assert.NotNull(result);
        Assert.Equal(expectedFormat, result.Cultures.First().Value);
        Assert.Equal(expectedUi, result.UICultures.First().Value);
    }

    [Fact]
    public async Task ProviderPrefersExplicitCultureCookie()
    {
        var context = new DefaultHttpContext();
        var cookieValue = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture("ru-RU"));
        context.Request.Headers.Cookie = $"{SupportedUiLanguages.CookieName}={Uri.EscapeDataString(cookieValue)}";
        context.Request.Headers.AcceptLanguage = "zh-CN";

        var result = await new UiRequestCultureProvider().DetermineProviderCultureResult(context);

        Assert.NotNull(result);
        Assert.Equal("ru-RU", result.Cultures.First().Value);
    }

    [Fact]
    public async Task ProviderPreservesSeparateFormattingAndInterfaceCultures()
    {
        var context = new DefaultHttpContext();
        var cookieValue = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture("en-GB", "zh-Hans"));
        context.Request.Headers.Cookie = $"{SupportedUiLanguages.CookieName}={Uri.EscapeDataString(cookieValue)}";

        var result = await new UiRequestCultureProvider().DetermineProviderCultureResult(context);

        Assert.NotNull(result);
        Assert.Equal("en-GB", result.Cultures.First().Value);
        Assert.Equal("zh-Hans", result.UICultures.First().Value);
    }

    [Fact]
    public void DefaultApiErrorsUseSelectedCultureAndKeepErrorCodesStable()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-Hans");

            var error = ApiErrorResponse.Create(StatusCodes.Status401Unauthorized, null, null, "trace");

            Assert.Equal("unauthorized", error.ErrorCode);
            Assert.Equal("需要身份验证。", error.Message);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("en-US", "Choose a supported currency.")]
    [InlineData("zh-Hans", "请选择支持的币种。")]
    public void KnownServiceErrorsUseSelectedCultureAndUnknownMessagesArePreserved(string cultureName, string expected)
    {
        var error = ApiErrorResponse.Create(400, "Выберите поддерживаемую валюту.", null, null, cultureName);
        var unknown = ApiErrorResponse.Create(400, "Example message", null, null, cultureName);

        Assert.Equal("bad_request", error.ErrorCode);
        Assert.Equal(expected, error.Message);
        Assert.Equal("Example message", unknown.Message);
    }

    [Theory]
    [InlineData("en-US", "The order currency differs from the organization currency. Financial changes are unavailable; amounts are not converted.")]
    [InlineData("zh-Hans", "订单币种与组织币种不同。无法修改财务数据，金额不会自动换算。")]
    public void HistoricalCurrencyRestrictionUsesSelectedLanguage(string cultureName, string expected)
    {
        var error = ApiErrorResponse.Create(400,
            "Валюта записи отличается от валюты организации. Финансовые изменения недоступны; конвертация не выполняется.",
            null, null, cultureName);

        Assert.Equal(expected, error.Message);
        Assert.Equal("bad_request", error.ErrorCode);
    }

    [Theory]
    [InlineData("en-US", "The draft was changed in another tab. Reopen it to avoid losing those changes.")]
    [InlineData("zh-Hans", "草稿已在其他标签页中更改。请重新打开，以免丢失其他人的修改。")]
    public void ImportConflictUsesSelectedLanguageAndPreservesHttpSemantics(string cultureName, string expected)
    {
        var error = ApiErrorResponse.Create(409,
            "Черновик изменён в другой вкладке. Откройте его заново, чтобы не потерять чужие правки.",
            null, "import-trace", cultureName);

        Assert.Equal(expected, error.Message);
        Assert.Equal("conflict", error.ErrorCode);
        Assert.Equal("import-trace", error.TraceId);
    }

    [Theory]
    [InlineData("en-US", "Sign in")]
    [InlineData("zh-Hans", "登录")]
    public void AuthenticationResourcesAreEmbeddedForSupportedLanguages(string cultureName, string expectedTitle)
    {
        var resources = new ResourceManager(
            "OpenCRM.WebApp.Resources.Infrastructure.Localization.LocalizationStrings",
            typeof(SupportedUiLanguages).Assembly);

        Assert.Equal(expectedTitle, resources.GetString("auth.loginTitle", CultureInfo.GetCultureInfo(cultureName)));
    }

    public static IEnumerable<object[]> TranslatedCultures => SupportedUiLanguages.Languages
        .Where(language => language.CultureName != "ru-RU")
        .Select(language => new object[] { language.CultureName });

    [Theory]
    [MemberData(nameof(TranslatedCultures))]
    public void ServerTranslationsContainAllKeysAndParameters(string cultureName)
    {
        SupportedUiLanguagesTests.AssertTranslations(new ResourceManager(
            "OpenCRM.WebApp.Resources.Infrastructure.Localization.LocalizationStrings",
            typeof(SupportedUiLanguages).Assembly), cultureName);
        SupportedUiLanguagesTests.AssertTranslations(new ResourceManager(
            "OpenCRM.Infrastructure.Auth.Resources.IdentityErrors",
            typeof(OpenCRM.Infrastructure.Auth.OpenCRMIdentityErrorDescriber).Assembly), cultureName);
    }

    private static void AssertTranslations(ResourceManager resources, string cultureName)
    {
        var russian = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false);
        var translated = resources.GetResourceSet(CultureInfo.GetCultureInfo(cultureName), true, false);
        Assert.NotNull(russian);
        Assert.NotNull(translated);
        var russianValues = russian.Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        var translatedValues = translated.Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);

        Assert.Equal(russianValues.Keys.Order(), translatedValues.Keys.Order());
        foreach (var (key, value) in russianValues)
        {
            Assert.False(string.IsNullOrWhiteSpace(translatedValues[key]), key);
            if (cultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                && !key.StartsWith("auth.language", StringComparison.Ordinal))
            {
                Assert.DoesNotMatch(@"[\u3400-\u9fffА-Яа-яЁё]", translatedValues[key]);
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(key, @"^[A-Za-z][A-Za-z0-9]*(?:[._-][A-Za-z0-9]+)+$"))
            {
                Assert.NotEqual(key, value);
                Assert.NotEqual(key, translatedValues[key]);
            }

            var sourceParameters = System.Text.RegularExpressions.Regex.Matches(value, @"\{\w+\}")
                .Select(match => match.Value).Distinct().Order();
            var translatedParameters = System.Text.RegularExpressions.Regex.Matches(translatedValues[key], @"\{\w+\}")
                .Select(match => match.Value).Distinct().Order();

            Assert.Equal(sourceParameters, translatedParameters);
        }
    }
}
