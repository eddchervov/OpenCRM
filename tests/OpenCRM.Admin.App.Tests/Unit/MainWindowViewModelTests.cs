using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.Api;

namespace OpenCRM.Admin.App.Tests.Unit;

public sealed class MainWindowViewModelTests
{
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
