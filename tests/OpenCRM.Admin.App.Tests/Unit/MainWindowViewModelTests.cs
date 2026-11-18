using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.Tests.Unit;

[Collection("Admin localization")]
public sealed class MainWindowViewModelTests
{
    [Fact]
    public void LanguageChangeTranslatesPermissionsAndConfigurationWithoutClearingCredentials()
    {
        var localization = AdminLocalization.Instance;
        var previousLanguage = localization.Language;
        var preferencePath = Path.Combine(Path.GetTempPath(), $"opencrm-shell-language-{Guid.NewGuid():N}.txt");
        using var preference = localization.UsePreferencePath(preferencePath);
        using var httpClient = new HttpClient { BaseAddress = new Uri("https://example.invalid/") };
        using var session = new AdminSessionClient(httpClient);
        var error = "Путь AdminDesktop:SessionDirectory должен быть абсолютным.";

        try
        {
            localization.Language = "ru";
            var model = new MainWindowViewModel(session, httpClient.BaseAddress, error)
            {
                Email = "operator@example.invalid",
                Password = "example-password"
            };
            model.Permissions.Add(new PermissionItem("Просмотр пользователей", "Admin.Users.View"));

            model.Language = "en";

            Assert.Equal("View users", model.Permissions.Single().Title);
            Assert.Contains("AdminDesktop:SessionDirectory must be an absolute path.", model.ErrorMessage);
            Assert.Equal("en", session.UiLanguage);

            model.Language = "zh-Hans";

            Assert.Equal("查看用户", model.Permissions.Single().Title);
            Assert.Contains("AdminDesktop:SessionDirectory 必须为绝对路径。", model.ErrorMessage);
            Assert.Equal("operator@example.invalid", model.Email);
            Assert.Equal("example-password", model.Password);
            Assert.False(model.LoginCommand.CanExecute(null));
        }
        finally
        {
            localization.Language = previousLanguage;
            File.Delete(preferencePath);
        }
    }

    [Fact]
    public void InvalidConfigurationDisablesLogin()
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri("https://localhost/") };
        var viewModel = new MainWindowViewModel(new AdminSessionClient(httpClient), httpClient.BaseAddress, "Некорректный адрес сервера.")
        {
            Email = "contact@example.invalid",
            Password =""
        };

        Assert.False(viewModel.LoginCommand.CanExecute(null));
        Assert.True(viewModel.HasError);
    }
}
