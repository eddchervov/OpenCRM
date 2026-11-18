using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.WebApp.Models.Organizations;

namespace OpenCRM.Hosting.Tests.Unit;

public sealed class OrganizationCreationLocalizationTests
{
    [Theory]
    [InlineData("en-US", "Choose a currency from the list.")]
    [InlineData("zh-Hans", "请从列表中选择币种。")]
    public void UnsupportedCurrencyValidationUsesInterfaceLanguage(string cultureName, string expected)
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddLocalization(options => options.ResourcesPath = "Resources")
            .BuildServiceProvider();
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var model = new CreateOrganizationInputModel { CurrencyCode = "JPY" };

            var error = Assert.Single(model.Validate(new ValidationContext(model, services, null)));

            Assert.Equal(expected, error.ErrorMessage);
            Assert.Equal(nameof(CreateOrganizationInputModel.CurrencyCode), Assert.Single(error.MemberNames));
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }
}
