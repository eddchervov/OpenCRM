using Avalonia.Data;
using Avalonia.Data.Converters;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Unit;

[Collection("Admin localization")]
public sealed class NativeOrganizationLocalizationTests
{
    [Fact]
    public void OrganizationScenarioAndRolesChangeLanguageWithoutChangingNamesOrCodes()
    {
        var localization = AdminLocalization.Instance;
        var previousLanguage = localization.Language;
        var preferencePath = Path.Combine(Path.GetTempPath(), $"opencrm-organization-language-{Guid.NewGuid():N}.txt");
        using var preference = localization.UsePreferencePath(preferencePath);
        using var http = new HttpClient { BaseAddress = new Uri("https://example.invalid/") };
        using var session = new AdminSessionClient(http);
        var organization = new AdminOrganization
        {
            Name = "Моя мастерская", PrimaryScenarioCode = "detailing", ScenarioName = "Детейлинг"
        };
        var row = new AdminOrganizationRow(organization);
        var model = new OrganizationsViewModel(session) { Organization = organization };
        var binding = Assert.IsType<MultiBinding>(new LocalizedOrganizationRoleExtension().ProvideValue(null!));
        var converter = Assert.IsAssignableFrom<IMultiValueConverter>(binding.Converter);

        try
        {
            localization.Language = "en";
            model.RefreshLocalizedValues();
            Assert.Equal("Detailing", row.Scenario);
            Assert.Equal("Detailing", model.OrganizationScenarioLabel);
            Assert.Equal("Owner", converter.Convert(["owner", "en"], typeof(string), null, System.Globalization.CultureInfo.CurrentCulture));

            localization.Language = "zh-Hans";
            model.RefreshLocalizedValues();
            Assert.Equal("汽车美容", row.Scenario);
            Assert.Equal("所有者", converter.Convert(["owner", "zh-Hans"], typeof(string), null, System.Globalization.CultureInfo.CurrentCulture));
            Assert.Equal("custom-role", converter.Convert(["custom-role", "zh-Hans"], typeof(string), null, System.Globalization.CultureInfo.CurrentCulture));
            Assert.Equal("Моя мастерская", organization.Name);
            Assert.Equal("detailing", organization.PrimaryScenarioCode);
            Assert.Equal("Исходный сценарий", new AdminOrganizationRow(new AdminOrganization
            {
                PrimaryScenarioCode = "custom", ScenarioName = "Исходный сценарий"
            }).Scenario);
        }
        finally
        {
            localization.Language = previousLanguage;
            File.Delete(preferencePath);
        }
    }
}
