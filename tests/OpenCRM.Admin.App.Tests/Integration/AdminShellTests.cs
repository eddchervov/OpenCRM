using System.Net;
using System.Net.Http.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class AdminShellTests
{
    [AvaloniaFact]
    public void LoginScreenMasksPasswordAndDoesNotExposeProfile()
    {
        using var httpClient = AdminShellTests.CreateHttpClient(new ShellApiHandler());
        var viewModel = new MainWindowViewModel(new AdminSessionClient(httpClient), httpClient.BaseAddress!);
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var password = AdminShellTests.FindControl<TextBox>(window, "PasswordInput");
            Assert.NotEqual(default, password.PasswordChar);
            Assert.False(viewModel.IsAuthenticated);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                control => AutomationProperties.GetAutomationId(control) == "ProfileEmail" && control.IsEffectivelyVisible);
            Assert.False(AdminShellTests.FindControl<Button>(window, "LoginButton").IsEffectivelyEnabled);
            AdminShellTests.SaveScreenshot(window, "login.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AuthorizedLoginShowsReadOnlyProfileAndPermissionFilteredMenu()
    {
        using var httpClient = AdminShellTests.CreateHttpClient(new ShellApiHandler());
        var viewModel = new MainWindowViewModel(new AdminSessionClient(httpClient), httpClient.BaseAddress!)
        {
            Email = "contact@example.invalid",
            Password ="test-password"
        };

        var window = new MainWindow { DataContext = viewModel, Title = "OpenCRM Admin — тестовый профиль" };
        window.Show();

        try
        {
            await viewModel.LoginCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.IsAuthenticated);
            Assert.Equal("contact@example.invalid", AdminShellTests.FindControl<TextBlock>(window, "ProfileEmail").Text);
            Assert.Empty(viewModel.Password);
            Assert.Equal(["Пользователи", "Организации", "Обращения"], viewModel.NavigationItems.Select(item => item.Title));
            var profile = window.GetVisualDescendants().OfType<ProfileView>().Single();
            Assert.Empty(profile.GetVisualDescendants().OfType<TextBox>());
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button =>
                viewModel.NavigationItems.Any(item => item.IsPlaceholder && item.Title == button.Content as string));
            AdminShellTests.SaveScreenshot(window, "profile.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task DeniedLoginDoesNotOpenShell(HttpStatusCode profileStatus)
    {
        using var httpClient = AdminShellTests.CreateHttpClient(new ShellApiHandler { ProfileStatus = profileStatus });
        var session = new AdminSessionClient(httpClient);
        var viewModel = new MainWindowViewModel(session, httpClient.BaseAddress!)
        {
            Email = "contact@example.invalid",
            Password ="test-password"
        };

        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        try
        {
            await viewModel.LoginCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(viewModel.IsAuthenticated);
            Assert.False(session.HasSession);
            Assert.True(viewModel.HasError);
            Assert.Empty(viewModel.NavigationItems);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                control => AutomationProperties.GetAutomationId(control) == "ProfileEmail" && control.IsEffectivelyVisible);
            if (profileStatus == HttpStatusCode.Forbidden)
            {
                AdminShellTests.SaveScreenshot(window, "access-denied.png");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RevokedAccessReturnsToLoginAndClearsProfile()
    {
        var handler = new ShellApiHandler();
        using var httpClient = AdminShellTests.CreateHttpClient(handler);
        var session = new AdminSessionClient(httpClient);
        var viewModel = new MainWindowViewModel(session, httpClient.BaseAddress!)
        {
            Email = "contact@example.invalid",
            Password ="test-password"
        };

        await viewModel.LoginCommand.ExecuteAsync(null);
        handler.ProfileStatus = HttpStatusCode.Forbidden;
        await viewModel.RefreshProfileCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsAuthenticated);
        Assert.False(session.HasSession);
        Assert.Empty(viewModel.Permissions);
        Assert.Empty(viewModel.NavigationItems);
        Assert.True(viewModel.HasError);
    }

    [AvaloniaFact]
    public async Task ClosingApplicationKeepsSessionWithoutRevokingToken()
    {
        var handler = new ShellApiHandler();
        using var httpClient = AdminShellTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(httpClient);
        var viewModel = new MainWindowViewModel(session, httpClient.BaseAddress!)
        {
            Email = "contact@example.invalid",
            Password ="test-password"
        };

        await viewModel.LoginCommand.ExecuteAsync(null);
        await viewModel.ShutdownAsync();

        Assert.False(handler.IsRevoked);
        Assert.True(session.HasSession);
        Assert.False(viewModel.LoginCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task RestoredSessionOpensShellAndSurvivesShutdown()
    {
        var handler = new ShellApiHandler();
        var store = new MemorySessionStore();
        using var httpClient = AdminShellTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(httpClient, sessionStore: store);
        var viewModel = new MainWindowViewModel(session, httpClient.BaseAddress!);

        await viewModel.RestoreSessionCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsAuthenticated);
        Assert.Equal("contact@example.invalid", viewModel.ProfileEmail);
        Assert.Empty(viewModel.Password);
        Assert.False(viewModel.HasError);
        Assert.NotEmpty(viewModel.NavigationItems);

        await viewModel.ShutdownAsync();

        Assert.NotNull(store.Session);
        Assert.False(handler.IsRevoked);
        Assert.False(viewModel.RestoreSessionCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task ExplicitLogoutDeletesSavedSessionAndRevokesToken()
    {
        var handler = new ShellApiHandler();
        var store = new MemorySessionStore();
        using var httpClient = AdminShellTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(httpClient, sessionStore: store);
        var viewModel = new MainWindowViewModel(session, httpClient.BaseAddress!);
        await viewModel.RestoreSessionCommand.ExecuteAsync(null);

        await viewModel.LogoutCommand.ExecuteAsync(null);

        Assert.Null(store.Session);
        Assert.True(handler.IsRevoked);
        Assert.False(viewModel.IsAuthenticated);
        Assert.Empty(viewModel.NavigationItems);
        await viewModel.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task FailedSessionDeletionIsShownInsteadOfSuccessfulLogout()
    {
        var handler = new ShellApiHandler();
        var store = new MemorySessionStore();
        using var httpClient = AdminShellTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(httpClient, sessionStore: store);
        var viewModel = new MainWindowViewModel(session, httpClient.BaseAddress!);
        await viewModel.RestoreSessionCommand.ExecuteAsync(null);
        store.CanDelete = false;

        await viewModel.LogoutCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasError);
        Assert.Contains("Не удалось удалить сохранённую сессию", viewModel.ErrorMessage);
        Assert.DoesNotContain("Вы вышли", viewModel.ErrorMessage);
        await viewModel.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task ShutdownCancelsRestoreAndPreservesRotatedToken()
    {
        var handler = new ShellApiHandler { DelayProfile = true };
        var store = new MemorySessionStore();
        using var httpClient = AdminShellTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(httpClient, sessionStore: store);
        var viewModel = new MainWindowViewModel(session, httpClient.BaseAddress!);
        var restore = viewModel.RestoreSessionCommand.ExecuteAsync(null);
        await handler.ProfileStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await viewModel.ShutdownAsync();
        await restore;

        Assert.False(viewModel.IsAuthenticated);
        Assert.False(handler.IsRevoked);
        Assert.Equal("test-refresh-token", store.Session?.RefreshToken);
    }

    [AvaloniaFact]
    public async Task ShutdownWaitsForRefreshResponseAndPersistsRotatedToken()
    {
        var handler = new ShellApiHandler { DelayRefresh = true };
        var store = new MemorySessionStore();
        using var httpClient = AdminShellTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(httpClient, sessionStore: store);
        var viewModel = new MainWindowViewModel(session, httpClient.BaseAddress!);
        var restore = viewModel.RestoreSessionCommand.ExecuteAsync(null);
        await handler.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var shutdown = viewModel.ShutdownAsync();

        Assert.False(shutdown.IsCompleted);
        handler.RefreshResponseReady.SetResult();
        await shutdown;
        await restore;

        Assert.False(viewModel.IsAuthenticated);
        Assert.False(handler.IsRevoked);
        Assert.Equal("test-refresh-token", store.Session?.RefreshToken);
    }

    [AvaloniaFact]
    public async Task SupportNavigationUsesDesktopScreenAndClearsOnRevokedAccess()
    {
        var handler = new ShellApiHandler();
        using var httpClient = AdminShellTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(httpClient);
        var viewModel = new MainWindowViewModel(session, httpClient.BaseAddress!)
        {
            Email = "contact@example.invalid",
            Password ="test-password"
        };

        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        try
        {
            await viewModel.LoginCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(AdminShellTests.FindControl<Button>(window, "SupportMessagesNavigation").IsEffectivelyVisible);
            Assert.False(AdminShellTests.FindControl<Button>(window, "EmailHistoryNavigation").IsEffectivelyVisible);
            Assert.False(AdminShellTests.FindControl<Button>(window, "RolesNavigation").IsEffectivelyVisible);
            Assert.All(viewModel.NavigationItems, item => Assert.False(item.IsPlaceholder));
            await viewModel.ShowRolesCommand.ExecuteAsync(null);
            await viewModel.ShowEmailHistoryCommand.ExecuteAsync(null);
            Assert.True(viewModel.IsProfileOpen);

            await viewModel.ShowSupportMessagesCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.IsSupportMessagesOpen);
            Assert.False(viewModel.IsProfileOpen);
            Assert.Equal("Обращения", viewModel.PageLabel);
            Assert.True(window.GetVisualDescendants().OfType<SupportMessagesView>().Single().IsEffectivelyVisible);
            Assert.Equal(1, handler.SupportRequests);
            AdminShellTests.SaveScreenshot(window, "support-messages.png");

            handler.Permissions = ["Admin.Access", "Admin.Users.View"];
            await viewModel.RefreshProfileCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(viewModel.CanViewSupportMessages);
            Assert.False(viewModel.IsSupportMessagesOpen);
            Assert.True(viewModel.IsProfileOpen);
            await viewModel.ShowSupportMessagesCommand.ExecuteAsync(null);
            Assert.Equal(1, handler.SupportRequests);
        }
        finally
        {
            await viewModel.ShutdownAsync();
            window.Close();
        }
    }
    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("https://demo.opencrm.local/")
    };

    private static T FindControl<T>(Window window, string id) where T : Control => window.GetVisualDescendants()
        .OfType<T>()
        .Single(control => AutomationProperties.GetAutomationId(control) == id);

    private static void SaveScreenshot(Window window, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("OPENCRM_ADMIN_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = File.Create(Path.Combine(directory, fileName));
        frame.Save(stream, PngBitmapEncoderOptions.Default);
    }

    private sealed class MemorySessionStore : IAdminSessionStore
    {
        public SavedAdminSession? Session { get; set; } =
            new("test-administrator", "saved-refresh-token", DateTimeOffset.UtcNow.AddDays(1));

        public bool CanDelete { get; set; } = true;

        public SavedAdminSession? Load() => this.Session;

        public void Save(SavedAdminSession session) => this.Session = session;

        public void Delete()
        {
            if (!this.CanDelete)
            {
                throw new ApiException("Не удалось удалить сохранённую сессию.");
            }

            this.Session = null;
        }
    }

    private sealed class ShellApiHandler : HttpMessageHandler
    {
        public bool DelayProfile { get; init; }

        public bool DelayRefresh { get; init; }

        public TaskCompletionSource RefreshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource RefreshResponseReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ProfileStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HttpStatusCode ProfileStatus { get; set; } = HttpStatusCode.OK;

        public string[] Permissions { get; set; } = ["Admin.Access", "Admin.Users.View", "Admin.Content.View"];

        public int SupportRequests { get; private set; }

        public bool IsRevoked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath is "/api/v1/auth/login" or "/api/v1/auth/refresh")
            {
                if (this.DelayRefresh && request.RequestUri.AbsolutePath == "/api/v1/auth/refresh")
                {
                    return this.WaitForRefreshResponseAsync(cancellationToken);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        accessToken = "test-access-token",
                        accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                        refreshToken = "test-refresh-token",
                        refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                        user = new { id = "test-administrator", email = "contact@example.invalid" }
                    })
                });
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/current-user")
            {
                if (this.DelayProfile)
                {
                    return this.WaitForProfileCancellationAsync(cancellationToken);
                }

                return Task.FromResult(new HttpResponseMessage(this.ProfileStatus)
                {
                    Content = this.ProfileStatus == HttpStatusCode.OK
                        ? JsonContent.Create(new AdminProfile
                        {
                            CommercialEnabled = true,
                            Id = "test-administrator",
                            Email = "contact@example.invalid",
                            Roles = ["Support"],
                            Permissions = this.Permissions
                        })
                        : JsonContent.Create(new { message = "Недостаточно прав для входа в административную панель." })
                });
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/support-messages")
            {
                this.SupportRequests++;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 50 })
                });
            }
            if (request.RequestUri.AbsolutePath == "/api/v1/auth/revoke")
            {
                this.IsRevoked = true;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/navigation/counts")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            throw new InvalidOperationException("Неожиданный запрос к тестовому серверу.");
        }

        private async Task<HttpResponseMessage> WaitForProfileCancellationAsync(CancellationToken cancellationToken)
        {
            this.ProfileStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            throw new InvalidOperationException("Ожидалась отмена запроса профиля.");
        }

        private async Task<HttpResponseMessage> WaitForRefreshResponseAsync(CancellationToken cancellationToken)
        {
            this.RefreshStarted.SetResult();
            await this.RefreshResponseReady.Task.WaitAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    accessToken = "test-access-token",
                    accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                    refreshToken = "test-refresh-token",
                    refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    user = new { id = "test-administrator", email = "contact@example.invalid" }
                })
            };
        }
    }
}
