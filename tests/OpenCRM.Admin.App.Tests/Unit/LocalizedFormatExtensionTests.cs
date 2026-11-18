using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.Tests.Unit;

[Collection("Admin localization")]
public sealed class LocalizedFormatExtensionTests
{
    [Fact]
    public void BindingFormatsValuesWithTheCurrentUiLanguageAndRegionalCulture()
    {
        var localization = AdminLocalization.Instance;
        var previousLanguage = localization.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var preferencePath = Path.Combine(Path.GetTempPath(), $"opencrm-admin-localized-format-{Guid.NewGuid():N}.txt");

        using var preferenceScope = localization.UsePreferencePath(preferencePath);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            localization.Language = "en";
            var binding = Assert.IsType<MultiBinding>(new LocalizedFormatExtension
            {
                Key = "native.billing.vsego.this.totalcount.n0",
                Path = "TotalCount"
            }.ProvideValue(null!));
            var converter = Assert.IsAssignableFrom<IMultiValueConverter>(binding.Converter);

            Assert.Equal("Total: 1,234", converter.Convert([1234, "en"], typeof(string), null, CultureInfo.CurrentCulture));

            localization.Language = "zh-Hans";

            Assert.Equal("总计：1,234", converter.Convert([1234, "zh-Hans"], typeof(string), null, CultureInfo.CurrentCulture));
            Assert.Equal(2, binding.Bindings.Count);
        }
        finally
        {
            localization.Language = previousLanguage;
            CultureInfo.CurrentCulture = previousCulture;
            if (File.Exists(preferencePath))
            {
                File.Delete(preferencePath);
            }
        }
    }

    [Fact]
    public void BindingPreservesSourceTextAndLocalizesFallbackValues()
    {
        var localization = AdminLocalization.Instance;
        var previousLanguage = localization.Language;
        var preferencePath = Path.Combine(Path.GetTempPath(), $"opencrm-admin-localized-fallback-{Guid.NewGuid():N}.txt");

        using var preferenceScope = localization.UsePreferencePath(preferencePath);
        try
        {
            localization.Language = "en";
            var binding = Assert.IsType<MultiBinding>(new LocalizedFormatExtension
            {
                Path = "SuggestedByEmail",
                FallbackKey = "native.promoCodes.polzovatel"
            }.ProvideValue(null!));
            var converter = Assert.IsAssignableFrom<IMultiValueConverter>(binding.Converter);

            Assert.Equal("custom value", converter.Convert(["custom value", "en"], typeof(string), null, CultureInfo.CurrentCulture));
            Assert.Equal(localization["native.promoCodes.polzovatel"], converter.Convert([null, "en"], typeof(string), null, CultureInfo.CurrentCulture));
            Assert.Equal(localization["native.promoCodes.polzovatel"], converter.Convert([AvaloniaProperty.UnsetValue, "en"], typeof(string), null, CultureInfo.CurrentCulture));
        }
        finally
        {
            localization.Language = previousLanguage;
            if (File.Exists(preferencePath))
            {
                File.Delete(preferencePath);
            }
        }
    }
}
