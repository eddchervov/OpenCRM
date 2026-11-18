using System.Globalization;
using OpenCRM.Infrastructure.Auth;

namespace OpenCRM.Hosting.Tests.Unit;

public sealed class IdentityLocalizationTests
{
    [Theory]
    [InlineData("ru-RU", "Пароль должен содержать не менее 12 символов.")]
    [InlineData("en-US", "The password must contain at least 12 characters.")]
    [InlineData("zh-Hans", "密码必须至少包含12个字符。")]
    public void IdentityErrorsKeepCodesAndFormatParametersInSelectedLanguage(string cultureName, string expected)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var describer = new OpenCRMIdentityErrorDescriber();
            var passwordError = describer.PasswordTooShort(12);
            var emailError = describer.DuplicateEmail("example@example.test");

            Assert.Equal("PasswordTooShort", passwordError.Code);
            Assert.Equal(expected, passwordError.Description);
            Assert.Equal("DuplicateEmail", emailError.Code);
            Assert.Contains("example@example.test", emailError.Description);
            Assert.DoesNotContain("{0}", emailError.Description);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}
