using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class NavigationReadTests
{
    [AvaloniaFact]
    public async Task SuccessfulVisitClearsOnlyThatSectionAndNewEventsAppearAgain()
    {
        using var handler = new ReadNavigationHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://read-navigation.test/") };
        using var client = new AdminSessionClient(http);
        var model = NavigationReadTests.CreateModel(client, http.BaseAddress);
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            Assert.Equal(3, model.TodayUsersCount);
            Assert.Equal(5, model.TodayEmailHistoryCount);

            await model.ShowUsersCommand.ExecuteAsync(null);

            Assert.Equal(0, model.TodayUsersCount);
            Assert.Equal(5, model.TodayEmailHistoryCount);
            Assert.Equal("users", Assert.Single(handler.ViewedSections));
            Assert.Equal(handler.SnapshotUtc, handler.ViewedThroughUtc);

            handler.Users = 1;
            model.StartNavigationUpdates(clearCounts: false);
            Assert.Equal(1, model.TodayUsersCount);
            await model.RefreshProfileCommand.ExecuteAsync(null);
            Assert.Equal(1, model.TodayUsersCount);
            Assert.Single(handler.ViewedSections);

            await model.Users.RefreshCommand.ExecuteAsync(null);

            Assert.Equal(0, model.TodayUsersCount);
            Assert.Equal(2, handler.ViewedSections.Count);
        }
        finally
        {
            await model.ShutdownAsync();
        }
    }

    [AvaloniaFact]
    public async Task FailedEmailLoadDoesNotAcknowledgeAndSuccessfulRefreshDoes()
    {
        using var handler = new ReadNavigationHandler { FailEmailLoad = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://read-navigation.test/") };
        using var client = new AdminSessionClient(http);
        var model = NavigationReadTests.CreateModel(client, http.BaseAddress);
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            await model.ShowEmailHistoryCommand.ExecuteAsync(null);

            Assert.False(string.IsNullOrWhiteSpace(model.EmailHistory.ErrorMessage));
            Assert.Equal(5, model.TodayEmailHistoryCount);
            Assert.Empty(handler.ViewedSections);

            handler.FailEmailLoad = false;
            await model.EmailHistory.RefreshCommand.ExecuteAsync(null);

            Assert.Equal(0, model.TodayEmailHistoryCount);
            Assert.Equal(3, model.TodayUsersCount);
            Assert.Equal("emails", Assert.Single(handler.ViewedSections));
        }
        finally
        {
            await model.ShutdownAsync();
        }
    }

    [AvaloniaFact]
    public async Task ViewingPaymentsAndSubscriptionsNeverAcknowledgesQueue()
    {
        using var handler = new ReadNavigationHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://read-navigation.test/") };
        using var client = new AdminSessionClient(http);
        var model = NavigationReadTests.CreateModel(client, http.BaseAddress);
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            model.Billing.IsPaymentsTab = false;
            await model.ShowBillingCommand.ExecuteAsync(null);

            Assert.Equal(2, model.TodayBillingCount);
            Assert.Empty(handler.ViewedSections);

            await model.Billing.SetTabCommand.ExecuteAsync(true);

            Assert.Equal(2, model.TodayBillingCount);
            Assert.Empty(handler.ViewedSections);

            handler.Payments = 0;
            model.StartNavigationUpdates(clearCounts: false);

            Assert.Equal(0, model.TodayBillingCount);
        }
        finally
        {
            await model.ShutdownAsync();
        }
    }

    [AvaloniaFact]
    public async Task FailedAcknowledgementKeepsBadgeAndReportsError()
    {
        using var handler = new ReadNavigationHandler { FailViewed = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://read-navigation.test/") };
        using var client = new AdminSessionClient(http);
        var model = NavigationReadTests.CreateModel(client, http.BaseAddress);
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            await model.ShowUsersCommand.ExecuteAsync(null);

            Assert.Equal(1, handler.UsersLoads);
            Assert.Equal(3, model.TodayUsersCount);
            Assert.Empty(handler.ViewedSections);
            Assert.Contains("не удалось сохранить отметку", model.ErrorMessage);

            handler.FailViewed = false;
            await model.Users.RefreshCommand.ExecuteAsync(null);

            Assert.Equal(0, model.TodayUsersCount);
            Assert.Empty(model.ErrorMessage);
        }
        finally
        {
            await model.ShutdownAsync();
        }
    }

    [AvaloniaFact]
    public async Task LeavingBeforeSnapshotReturnsDoesNotLoadOrAcknowledgeSection()
    {
        using var handler = new ReadNavigationHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://read-navigation.test/") };
        using var client = new AdminSessionClient(http);
        var model = NavigationReadTests.CreateModel(client, http.BaseAddress);
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            handler.HoldNextCounts = true;
            var navigation = model.ShowUsersCommand.ExecuteAsync(null);
            await handler.CountsHeld.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var leaving = model.ShowProfileCommand.ExecuteAsync(null);
            handler.ReleaseCounts.TrySetResult();
            await Task.WhenAll(navigation, leaving).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Empty(handler.ViewedSections);
            Assert.Equal(0, handler.UsersLoads);
            Assert.Equal(3, model.TodayUsersCount);
        }
        finally
        {
            handler.ReleaseCounts.TrySetResult();
            await model.ShutdownAsync();
        }
    }

    [AvaloniaFact]
    public async Task LatePollingResponseCannotRestoreAcknowledgedBadge()
    {
        using var handler = new ReadNavigationHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://read-navigation.test/") };
        using var client = new AdminSessionClient(http);
        var model = NavigationReadTests.CreateModel(client, http.BaseAddress);
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            handler.HoldNextCounts = true;
            model.StartNavigationUpdates(clearCounts: false);
            var oldPoll = model.NavigationUpdatesTask;
            await handler.CountsHeld.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await model.ShowUsersCommand.ExecuteAsync(null);
            Assert.Equal(0, model.TodayUsersCount);
            handler.ReleaseCounts.TrySetResult();
            await oldPoll;

            Assert.Equal(0, model.TodayUsersCount);
        }
        finally
        {
            handler.ReleaseCounts.TrySetResult();
            await model.ShutdownAsync();
        }
    }

    private static MainWindowViewModel CreateModel(AdminSessionClient client, Uri serverAddress) => new(client, serverAddress)
    {
        Email = "contact@example.invalid", Password =""
    };

    private sealed class ReadNavigationHandler : HttpMessageHandler
    {
        public DateTime SnapshotUtc { get; } = DateTime.UtcNow.AddMinutes(-1);

        public int Users { get; set; } = 3;

        public int Emails { get; set; } = 5;

        public int Payments { get; set; } = 2;

        public bool FailEmailLoad { get; set; }

        public bool FailViewed { get; set; }

        public bool HoldNextCounts { get; set; }

        public int UsersLoads { get; private set; }

        public DateTime? ViewedThroughUtc { get; private set; }

        public List<string> ViewedSections { get; } = [];

        public TaskCompletionSource CountsHeld { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseCounts { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
            {
                return ReadNavigationHandler.Reply(new
                {
                    accessToken = "test-access", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    refreshToken = "test-refresh", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    user = new { id = "admin", email = "contact@example.invalid" }
                });
            }

            if (path == "/api/v1/admin/current-user")
            {
                return ReadNavigationHandler.Reply(new AdminProfile
                {
                    CommercialEnabled = true,
                    Id = "admin", Email = "contact@example.invalid",
                    Permissions = ["Admin.Access", "Admin.Users.View", "Admin.Communications.View", "Admin.Billing.View"]
                });
            }

            if (path == "/api/v1/admin/navigation/counts")
            {
                Assert.Contains("unread=true", request.RequestUri.Query);
                var reply = ReadNavigationHandler.Reply(new
                {
                    users = this.Users, emails = this.Emails, payments = this.Payments,
                    pendingCatalogSuggestions = 7, asOfUtc = this.SnapshotUtc
                });

                if (this.HoldNextCounts)
                {
                    this.HoldNextCounts = false;
                    this.CountsHeld.TrySetResult();
                    // Имитируем поздний ответ транспорта, который не успел обработать отмену.
                    await this.ReleaseCounts.Task;
                }

                return reply;
            }

            if (path.StartsWith("/api/v1/admin/navigation/sections/", StringComparison.Ordinal))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                if (this.FailViewed)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }

                var section = path.Split('/')[6];
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                this.ViewedThroughUtc = json.RootElement.GetProperty("viewedThroughUtc").GetDateTime();
                this.ViewedSections.Add(section);
                if (section == "users")
                {
                    Assert.True(this.UsersLoads > 0);
                    this.Users = 0;
                }
                else if (section == "emails")
                {
                    this.Emails = 0;
                }
                else if (section == "payments")
                {
                    this.Payments = 0;
                }

                return ReadNavigationHandler.Reply(new { message = "Просмотр сохранён." });
            }

            if (path == "/api/v1/admin/users")
            {
                this.UsersLoads++;

                return ReadNavigationHandler.Reply(new AdminUsersResponse { Page = 1, PageSize = 50 });
            }

            if (path == "/api/v1/admin/email-messages")
            {
                return this.FailEmailLoad
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : ReadNavigationHandler.Reply(new EmailMessagesPageResponse { Page = 1, PageSize = 50 });
            }

            if (path == "/api/v1/admin/billing/payments")
            {
                return ReadNavigationHandler.Reply(new AdminBillingPaymentsPage { Page = 1, PageSize = 50 });
            }

            if (path == "/api/v1/admin/billing/subscriptions/organizations")
            {
                return ReadNavigationHandler.Reply(new AdminBillingSubscriptionsPage { Page = 1, PageSize = 50 });
            }

            if (path == "/api/v1/auth/revoke")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            throw new InvalidOperationException("Неожиданный тестовый запрос: " + path);
        }

        private static HttpResponseMessage Reply(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
