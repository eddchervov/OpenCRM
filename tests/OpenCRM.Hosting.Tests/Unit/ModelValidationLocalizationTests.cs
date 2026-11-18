using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Resources;
using OpenCRM.WebApp.Infrastructure.Localization;

namespace OpenCRM.Hosting.Tests.Unit;

public sealed class ModelValidationLocalizationTests
{
    [Theory]
    [MemberData(nameof(SupportedUiLanguagesTests.TranslatedCultures), MemberType = typeof(SupportedUiLanguagesTests))]
    public void ExplicitModelValidationMessagesHaveTranslations(string cultureName)
    {
        var resources = new ResourceManager(
            "OpenCRM.WebApp.Resources.Infrastructure.Localization.LocalizationStrings",
            typeof(LocalizationStrings).Assembly);
        var culture = CultureInfo.GetCultureInfo(cultureName);
        var messages = typeof(LocalizationStrings).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("OpenCRM.WebApp.Models", StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetProperties())
            .SelectMany(property => property.GetCustomAttributes<ValidationAttribute>())
            .Where(attribute => attribute.ErrorMessageResourceType == null && attribute.ErrorMessage != null)
            .Select(attribute => attribute.ErrorMessage!)
            .Distinct();

        foreach (var message in messages)
        {
            var translated = resources.GetString(message, culture);

            Assert.False(string.IsNullOrWhiteSpace(translated), message);
            Assert.NotEqual(message, translated);
        }
    }
}
