using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.Tests.Unit;

public sealed class AdminLocalizationTests
{
    [Theory]
    [InlineData("ru", "Мой профиль")]
    [InlineData("en", "My profile")]
    [InlineData("zh-Hans", "我的资料")]
    public void LanguageChangesTranslateMainNavigationWithoutChangingSupportedCodes(string language, string expected)
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"opencrm-admin-language-{Guid.NewGuid():N}.txt");
        var localization = new AdminLocalization(settingsPath, "ru");
        try
        {
            localization.Language = "en";
            localization.Language = language;

            Assert.Equal(expected, localization["nav.profile"]);
            Assert.Equal(new[] { "ru", "en", "zh-Hans" }, localization.Languages.Select(option => option.Code));
            Assert.Equal(language, File.ReadAllText(settingsPath));
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public void UnsupportedLanguageFallsBackToRussian()
    {
        var localization = new AdminLocalization(Path.Combine(Path.GetTempPath(), $"opencrm-admin-language-{Guid.NewGuid():N}.txt"), "ru");

        localization.Language = "fr";

        Assert.Equal("ru", localization.Language);
        Assert.Equal("Мой профиль", localization["nav.profile"]);
    }

    [Fact]
    public void MainShellAndLoginHaveTranslationsForEveryRegisteredLanguage()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"opencrm-admin-language-{Guid.NewGuid():N}.txt");
        var localization = new AdminLocalization(settingsPath, "ru");
        string[] keys =
        [
            "app.title", "app.subtitle", "nav.profile", "nav.sections", "nav.dashboard", "nav.users",
            "nav.organizations", "nav.billing", "nav.support", "nav.emailHistory", "nav.plans", "nav.promoCodes",
            "nav.roles", "nav.logout", "nav.newSinceLastVisit", "nav.billingAttention", "nav.pendingCatalog",
            "nav.productSections", "nav.placeholderTooltip", "nav.placeholderLater", "users.cardPage",
            "profile.systemAdmin", "profile.adminAccess", "operation.compose", "operation.campaigns", "operation.smsHistory",
            "operation.siteNotifications", "operation.vehicleCatalog", "operation.catalogSuggestions", "operation.testData",
            "operation.crmImport", "operation.assistant", "operation.userActivity", "login.title", "login.email",
            "login.password", "login.submit"
        ];

        try
        {
            foreach (var language in localization.Languages)
            {
                localization.Language = language.Code;
                Assert.All(keys, key => Assert.NotEqual(key, localization[key]));
            }
        }
        finally
        {
            if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }
        }
    }
}
