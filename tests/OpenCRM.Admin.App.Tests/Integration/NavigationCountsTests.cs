using System.Net;
using System.Net.Http.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class NavigationCountsTests
{
    [AvaloniaFact]
    public async Task MinimizedWindowPausesBackgroundRequestsAndRestoringResumesCounts()
    {
        var handler = new NavigationCountsHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        var model = new MainWindowViewModel(client, http.BaseAddress) { Email = "contact@example.invalid", Password ="" };
        var window = new MainWindow { DataContext = model, WindowState = WindowState.Minimized };

        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            var profileRequests = handler.ProfileRequests;
            await model.RefreshBackgroundProfileAsync();
            Assert.Equal(profileRequests, handler.ProfileRequests);
            Assert.Equal(0, handler.CountRequests);

            window.WindowState = WindowState.Normal;
            await handler.CountRequest.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(1, handler.CountRequests);
            Assert.Equal(3, model.TodayUsersCount);

            window.WindowState = WindowState.Minimized;
            await model.NavigationUpdatesTask;
            await model.RefreshBackgroundProfileAsync();
            Assert.Equal(profileRequests, handler.ProfileRequests);
            Assert.Equal(1, handler.CountRequests);
            Assert.Equal(3, model.TodayUsersCount);

            handler.CountsStatus = HttpStatusCode.ServiceUnavailable;
            window.WindowState = WindowState.Normal;
            await handler.CountsFailureReturned.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(3, model.TodayUsersCount);
            handler.CountsStatus = HttpStatusCode.OK;
            await model.RefreshBackgroundProfileAsync();
            Assert.Equal(profileRequests + 1, handler.ProfileRequests);
            Assert.True(handler.CountRequests > 1);
        }
        finally
        {
            await model.ShutdownAsync();
            window.Close();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModerationUpdatesQueueBadgeAndLogoutClearsIt(bool switchTabDuringReview)
    {
        var handler = new NavigationCountsHandler
        {
            Permissions = ["Admin.Access", "Admin.Content.View", "Admin.Content.Manage"],
            HoldReview = switchTabDuringReview
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        var model = new MainWindowViewModel(client, http.BaseAddress) { Email = "contact@example.invalid", Password ="" };
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            var section = model.VisibleOperationSections.Single(item => item.Model is CatalogSuggestionsViewModel);
            await model.ShowOperationCommand.ExecuteAsync(section);
            var suggestions = (CatalogSuggestionsViewModel)section.Model;
            Assert.Equal(7, section.BadgeCount);
            suggestions.SelectSuggestionCommand.Execute(suggestions.Suggestions[0]);
            suggestions.RequestApproveCommand.Execute(null);
            var review = suggestions.ConfirmReviewCommand.ExecuteAsync(null);
            if (switchTabDuringReview)
            {
                await handler.ReviewStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                suggestions.SelectedTabIndex = 1;
                var loadTab = suggestions.LoadSelectedTabAsync();
                handler.ReleaseReview.TrySetResult();
                await Task.WhenAll(review, loadTab);
            }
            else
            {
                await review;
            }
            Assert.Empty(suggestions.ErrorMessage);
            Assert.Equal(6, section.BadgeCount);
            Assert.True(section.HasBadge);
            await model.LogoutCommand.ExecuteAsync(null);
            Assert.Equal(0, section.BadgeCount);
            Assert.False(section.HasBadge);
        }
        finally
        {
            await model.ShutdownAsync();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoginShowsTodayCountsOnTheRightOfNavigationItems(bool legacyCounts)
    {
        var handler = new NavigationCountsHandler { LegacyCounts = legacyCounts };
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var sessionClient = new AdminSessionClient(httpClient);
        var viewModel = new MainWindowViewModel(sessionClient, httpClient.BaseAddress!);
        var window = new MainWindow { DataContext = viewModel, Title = "OpenCRM Admin — счётчики меню" };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var countsUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(MainWindowViewModel.TodaySupportMessagesCount))
            {
                countsUpdated.TrySetResult();
            }
        };
        viewModel.Email = "contact@example.invalid";
        viewModel.Password ="";

        try
        {
            await viewModel.LoginCommand.ExecuteAsync(null);
            await countsUpdated.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(3, viewModel.TodayUsersCount);
            Assert.Equal(2, viewModel.TodayBillingCount);
            Assert.Equal(4, viewModel.TodaySupportMessagesCount);
            Assert.Equal(0, viewModel.TodayEmailHistoryCount);
            Assert.True(viewModel.HasTodayUsers);
            Assert.True(viewModel.HasTodayBilling);
            Assert.True(viewModel.HasTodaySupportMessages);
            Assert.False(viewModel.HasTodayEmailHistory);
            Assert.Equal(1, handler.CountRequests);
            var suggestions = viewModel.VisibleOperationSections.Single(section => section.Model is CatalogSuggestionsViewModel);
            Assert.Equal(7, suggestions.BadgeCount);
            Assert.True(suggestions.HasBadge);
            if (legacyCounts)
            {
                Assert.Equal("?status=1", handler.LastSuggestionsUri?.Query);
            }
            var suggestionsNavigation = window.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.CommandParameter, suggestions));
            Assert.True(NavigationCountsTests.FindBadge(suggestionsNavigation, "7").IsEffectivelyVisible);
            var query = handler.LastCountsUri!.Query.TrimStart('?').Split('&')
                .Select(value => value.Split('=', 2))
                .ToDictionary(value => value[0], value => Uri.UnescapeDataString(value[1]));
            var localDate = DateTime.Today;
            Assert.Equal(localDate.ToUniversalTime(), DateTime.Parse(query["fromUtc"], null, System.Globalization.DateTimeStyles.RoundtripKind));
            Assert.Equal(localDate.AddDays(1).ToUniversalTime(), DateTime.Parse(query["toUtc"], null, System.Globalization.DateTimeStyles.RoundtripKind));

            var usersNavigation = NavigationCountsTests.FindControl<Button>(window, "UsersNavigation");
            var usersLabel = usersNavigation.GetVisualDescendants().OfType<TextBlock>()
                .Single(textBlock => textBlock.Text == "Пользователи");
            var usersBadge = NavigationCountsTests.FindBadge(usersNavigation, "3");
            Assert.True(usersBadge.IsEffectivelyVisible);
            Assert.True(usersBadge.TranslatePoint(new Point(0, 0), window)!.Value.X
                > usersLabel.TranslatePoint(new Point(0, 0), window)!.Value.X + usersLabel.Bounds.Width);

            var emailNavigation = NavigationCountsTests.FindControl<Button>(window, "EmailHistoryNavigation");
            Assert.False(NavigationCountsTests.FindBadge(emailNavigation, "0").IsEffectivelyVisible);
            NavigationCountsTests.SaveScreenshot(window, "admin-navigation-today.png");
        }
        finally
        {
            await viewModel.ShutdownAsync();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CountsAreHiddenWhenProfileCannotViewSections()
    {
        var handler = new NavigationCountsHandler
        {
            Permissions = ["Admin.Access", "Admin.Users.View"]
        };
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var sessionClient = new AdminSessionClient(httpClient);
        var viewModel = new MainWindowViewModel(sessionClient, httpClient.BaseAddress!);
        var countsUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(MainWindowViewModel.TodayUsersCount))
            {
                countsUpdated.TrySetResult();
            }
        };
        viewModel.Email = "contact@example.invalid";
        viewModel.Password ="";

        try
        {
            await viewModel.LoginCommand.ExecuteAsync(null);
            await countsUpdated.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Equal(3, viewModel.TodayUsersCount);
            Assert.Equal(0, viewModel.TodayBillingCount);
            Assert.Equal(0, viewModel.TodaySupportMessagesCount);
            Assert.Equal(0, viewModel.TodayEmailHistoryCount);
            Assert.True(viewModel.HasTodayUsers);
            Assert.False(viewModel.HasTodayBilling);
            Assert.False(viewModel.HasTodaySupportMessages);
            Assert.False(viewModel.HasTodayEmailHistory);
        }
        finally
        {
            await viewModel.ShutdownAsync();
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task NavigationApiErrorsDoNotBreakLoginOrShutdown(HttpStatusCode responseStatus)
    {
        var handler = new NavigationCountsHandler { CountsStatus = responseStatus };
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var sessionClient = new AdminSessionClient(httpClient);
        var viewModel = new MainWindowViewModel(sessionClient, httpClient.BaseAddress!);
        viewModel.Email = "contact@example.invalid";
        viewModel.Password ="";

        await viewModel.LoginCommand.ExecuteAsync(null);
        await handler.CountRequest.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsAuthenticated);
        Assert.Equal(1, handler.CountRequests);
        await viewModel.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task UnauthorizedNavigationRequestClearsExpiredSession()
    {
        var handler = new NavigationCountsHandler { CountsStatus = HttpStatusCode.Unauthorized };
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var sessionClient = new AdminSessionClient(httpClient);
        var viewModel = new MainWindowViewModel(sessionClient, httpClient.BaseAddress!);
        var sessionExpired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(MainWindowViewModel.IsAuthenticated) && !viewModel.IsAuthenticated)
            {
                sessionExpired.TrySetResult();
            }
        };
        viewModel.Email = "contact@example.invalid";
        viewModel.Password ="";

        await viewModel.LoginCommand.ExecuteAsync(null);
        await handler.CountRequest.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await sessionExpired.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(viewModel.IsAuthenticated);
        Assert.False(sessionClient.HasSession);
        Assert.Equal(0, viewModel.TodayUsersCount);
        Assert.Equal(2, handler.CountRequests);
        await viewModel.NavigationUpdatesTask;
    }

    private static T FindControl<T>(Window window, string id) where T : Control => window.GetVisualDescendants()
        .OfType<T>()
        .Single(control => AutomationProperties.GetAutomationId(control) == id);

    private static Border FindBadge(Button navigation, string count) => navigation.GetVisualDescendants()
        .OfType<Border>()
        .Single(border => border.Child is TextBlock textBlock && textBlock.Text == count);

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

    private sealed class NavigationCountsHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _countRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HttpStatusCode CountsStatus { get; set; } = HttpStatusCode.OK;

        public bool LegacyCounts { get; init; }

        private bool _hasReviewedSuggestion;

        public bool HoldReview { get; init; }

        public TaskCompletionSource ReviewStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseReview { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string[] Permissions { get; init; } =
        [
            "Admin.Access",
            "Admin.Users.View",
            "Admin.Billing.View",
            "Admin.Content.View",
            "Admin.Communications.View"
        ];

        public int CountRequests { get; private set; }

        public int ProfileRequests { get; private set; }

        public Uri? LastCountsUri { get; private set; }

        public Uri? LastSuggestionsUri { get; private set; }

        public Task CountRequest => this._countRequest.Task;

        public TaskCompletionSource CountsFailureReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath is "/api/v1/auth/login" or "/api/v1/auth/refresh")
            {
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
                this.ProfileRequests++;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new AdminProfile
                    {
                        Id = "test-administrator",
                        Email = "contact@example.invalid",
                        CommercialEnabled = true,
                        Permissions = this.Permissions
                    })
                });
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/navigation/counts")
            {
                this.CountRequests++;
                this.LastCountsUri = request.RequestUri;
                this._countRequest.TrySetResult();

                var countsStatus = this.CountsStatus;
                if (countsStatus != HttpStatusCode.OK)
                {
                    this.CountsFailureReturned.TrySetResult();

                    return Task.FromResult(new HttpResponseMessage(countsStatus)
                    {
                        Content = JsonContent.Create(new { message = "Ошибка тестового API." })
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new AdminNavigationCounts
                    {
                        Users = 3,
                        Payments = 2,
                        SupportMessages = 4,
                        Emails = 0,
                        PendingCatalogSuggestions = this.LegacyCounts ? null : 7
                    })
                });
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/auth/revoke")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/catalog-suggestions/1/approve")
            {
                this._hasReviewedSuggestion = true;

                if (this.HoldReview)
                {
                    this.ReviewStarted.TrySetResult();

                    return this.WaitForReviewAsync(cancellationToken);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { message = "Предложение одобрено." })
                });
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/catalog-suggestions")
            {
                this.LastSuggestionsUri = request.RequestUri;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(Enumerable.Range(1, 7)
                        .Where(id => !this._hasReviewedSuggestion || id != 1)
                        .Select(id => new AdminCatalogSuggestion
                    {
                        Id = id,
                        Status = 1,
                        CreatedAt = DateTime.UtcNow.AddDays(-id)
                    }))
                });
            }

            throw new InvalidOperationException("Неожиданный запрос к тестовому серверу.");
        }

        private async Task<HttpResponseMessage> WaitForReviewAsync(CancellationToken cancellationToken)
        {
            await this.ReleaseReview.Task.WaitAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { message = "Предложение одобрено." })
            };
        }
    }
}
