using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.Tests.Unit;

public sealed class NativeResourceCoverageTests
{
    public static IEnumerable<object[]> TranslatedLanguages => AdminUiLanguage.Supported
        .Where(language => language.Code != "ru")
        .Select(language => new object[] { language.Code });

    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void EveryNativeCatalogContainsAllKeysAndParameters(string language)
    {
        NativeResourceCoverageTests.AssertCatalog(new ResourceManager(
            "OpenCRM.Admin.App.Localization.Strings", typeof(AdminLocalization).Assembly), language);
        NativeResourceCoverageTests.AssertCatalog(new ResourceManager(
            "OpenCRM.Admin.Api.Strings", typeof(AdminSessionClient).Assembly), language);
    }

    private static void AssertCatalog(ResourceManager resources, string language)
    {
        var russian = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false);
        var translated = resources.GetResourceSet(CultureInfo.GetCultureInfo(language), true, false);
        Assert.NotNull(russian);
        Assert.NotNull(translated);
        var source = russian.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
        var target = translated.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);

        Assert.Equal(source.Keys.Order(), target.Keys.Order());
        foreach (var (key, value) in source)
        {
            Assert.False(string.IsNullOrWhiteSpace(target[key]), key);
            Assert.Equal(NativeResourceCoverageTests.Parameters(value), NativeResourceCoverageTests.Parameters(target[key]));
            if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase) && value != "На базе OpenCRM")
            {
                Assert.DoesNotMatch(@"[\u3400-\u9fffА-Яа-яЁё]", target[key]);
            }
        }
    }

    private static IEnumerable<string> Parameters(string value) => Regex.Matches(value, @"\{(\w+)(?:,[^}:]+)?(?::[^}]+)?\}")
        .Select(match => match.Groups[1].Value).Distinct().Order();
}
