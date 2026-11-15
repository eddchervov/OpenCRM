using Notifications.Infrastructure.Templating;

namespace Notifications.Tests.Unit;

/// <summary>Проверяет схему ссылки мобильного приложения в профиле системных писем.</summary>
public sealed class SystemTemplateOptionsValidatorTests
{
    [Theory]
    [InlineData(" ")]
    [InlineData("sample-crm:")]
    [InlineData("//example.com")]
    [InlineData("sample_crm")]
    [InlineData("sample crm")]
    [InlineData("sample\r\ncrm")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void InvalidMobileDeepLinkSchemeIsRejected(string scheme)
    {
        var options = new SystemTemplateOptions
        {
            Applications = new Dictionary<string, SystemTemplateApplicationOptions>(StringComparer.Ordinal)
            {
                ["sample-crm"] = new()
                {
                    BrandName = "Sample CRM",
                    MobileDeepLinkScheme = scheme,
                    Templates = ["system/password-reset-mobile"]
                }
            }
        };

        var result = new SystemTemplateOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("opencrm")]
    [InlineData("sample-crm")]
    [InlineData("sample+crm.2")]
    public void ValidMobileDeepLinkSchemeIsAccepted(string scheme)
    {
        var options = new SystemTemplateOptions
        {
            Applications = new Dictionary<string, SystemTemplateApplicationOptions>(StringComparer.Ordinal)
            {
                ["sample-crm"] = new()
                {
                    BrandName = "Sample CRM",
                    MobileDeepLinkScheme = scheme,
                    Templates = ["system/password-reset-mobile"]
                }
            }
        };

        var result = new SystemTemplateOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }
}
