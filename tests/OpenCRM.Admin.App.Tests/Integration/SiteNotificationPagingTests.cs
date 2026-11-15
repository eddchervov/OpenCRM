using System.Net;
using System.Net.Http.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCRM.Admin.App.Views;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class SiteNotificationPagingTests
{
    [AvaloniaFact]
    public async Task NotificationTabsStretchAndOpenTheirContent()
    {
        using var handler = new SiteNotificationHandler();
        using var client = await SiteNotificationPagingTests.CreateClientAsync(handler);
        var model = new SiteNotificationsViewModel(client);
        var view = new SiteNotificationsView { DataContext = model };
        var window = new Window { Width = 1160, Height = 850, Content = view };
        window.Show();
        try
        {
            await model.ActivateAsync();
            var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
            for (var index = 0; index < 3; index++)
            {
                tabs.SelectedIndex = index;
                await model.LoadSelectedTabAsync();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(index, model.SelectedTabIndex);
                var content = view.GetVisualDescendants().OfType<ScrollViewer>()
                    .First(control => control.IsEffectivelyVisible);
                Assert.True(content.Bounds.Width > 1000);
                var directory = Environment.GetEnvironmentVariable("OPENCRM_ADMIN_SCREENSHOTS");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var bitmap = window.CaptureRenderedFrame();
                    Assert.NotNull(bitmap);
                    using var stream = File.Create(Path.Combine(directory, $"notifications-tab-{index}.png"));
                    bitmap.Save(stream, PngBitmapEncoderOptions.Default);
                }
            }
        }
        finally
        {
            await model.DeactivateAsync(true);
            window.Close();
        }
    }

    [Fact]
    public async Task LegacyServerResponseFallsBackToLocalPages()
    {
        using var handler = new SiteNotificationHandler { LegacyMode = true };
        using var client = await SiteNotificationPagingTests.CreateClientAsync(handler);

        var history = await client.GetSiteNotificationHistoryAsync(2, 10, TestContext.Current.CancellationToken);
        var templates = await client.GetSiteNotificationTemplatesAsync(TestContext.Current.CancellationToken);

        Assert.True(history.IsLegacyResponse);
        Assert.Equal(12, history.TotalCount);
        Assert.Equal(2, history.Page);
        Assert.Equal(2, history.Items.Count);
        Assert.All(history.Items, notification => Assert.False(notification.IsTemplate));
        Assert.All(templates, notification => Assert.True(notification.IsTemplate));
        Assert.Contains(handler.Requests, request => request == "/api/v1/admin/site-notifications");
    }

    [Fact]
    public async Task TabsLoadTemplatesAndPagedHistorySeparatelyAndPreserveSendGuard()
    {
        using var handler = new SiteNotificationHandler();
        using var client = await SiteNotificationPagingTests.CreateClientAsync(handler);
        var model = new SiteNotificationsViewModel(client);

        await model.ActivateAsync();
        Assert.Equal(0, model.SelectedTabIndex);
        Assert.DoesNotContain(handler.Requests, request => request.Contains("site-notifications", StringComparison.Ordinal));

        model.RecipientScope = model.RecipientScopes.Single(option => option.Code == "selected");
        model.UserSearch = "client";
        await model.SearchUsersCommand.ExecuteAsync(null);
        model.FoundUsers.Single().IsSelected = true;
        model.SendTitle = "Проверка";
        model.SendBody = "Текст";
        handler.FailNextSend = true;
        model.RequestSendCommand.Execute(null);
        await model.ConfirmSendCommand.ExecuteAsync(null);
        Assert.True(model.IsSendAttempted);
        Assert.False(model.IsBusy);
        Assert.True(model.RefreshCommand.CanExecute(null));

        model.SelectedTabIndex = 1;
        await model.LoadSelectedTabAsync();
        await SiteNotificationPagingTests.WaitForAsync(() => handler.Requests.Any(request => request.EndsWith("/history?page=1&pageSize=20", StringComparison.Ordinal)));
        await SiteNotificationPagingTests.WaitForAsync(
            () => model.HistoryItems.Count == 20,
            $"История не загрузилась. Ошибка: {model.ErrorMessage}; запросы: {string.Join(", ", handler.Requests)}");
        Assert.Equal(25, model.TotalCount);
        Assert.Equal(20, model.HistoryItems.Count);
        Assert.Contains("/api/v1/admin/site-notifications/history?page=1&pageSize=20", handler.Requests);
        Assert.True(model.NextHistoryPageCommand.CanExecute(null));
        await model.NextHistoryPageCommand.ExecuteAsync(null);
        await SiteNotificationPagingTests.WaitForAsync(() => handler.Requests.Any(request => request.EndsWith("/history?page=2&pageSize=20", StringComparison.Ordinal)));
        Assert.Equal(2, model.Page);
        Assert.Equal("user-1", Assert.Single(model.SelectedUsers).Id);
        Assert.Equal("Проверка", model.SendTitle);

        model.SelectedTabIndex = 2;
        await model.LoadSelectedTabAsync();
        await SiteNotificationPagingTests.WaitForAsync(() => handler.Requests.Any(request => request.EndsWith("/templates", StringComparison.Ordinal)));
        await SiteNotificationPagingTests.WaitForAsync(
            () => model.Templates.Count == 1,
            $"Шаблоны не загрузились. Ошибка: {model.ErrorMessage}; запросы: {string.Join(", ", handler.Requests)}");
        Assert.Single(model.Templates);
        Assert.Contains("/api/v1/admin/site-notifications/templates", handler.Requests);

        model.SelectedTabIndex = 0;
        Assert.True(model.IsSendAttempted);
        Assert.Equal("user-1", Assert.Single(model.SelectedUsers).Id);
        Assert.Equal("Проверка", model.SendTitle);
        Assert.DoesNotContain(handler.Requests, request => request == "/api/v1/admin/site-notifications");
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task FastTabChangeLoadsLatestTabAfterHistoryRequestCompletes()
    {
        using var handler = new SiteNotificationHandler { HoldHistoryResponse = true };
        using var client = await SiteNotificationPagingTests.CreateClientAsync(handler);
        var model = new SiteNotificationsViewModel(client);
        await model.ActivateAsync();

        model.SelectedTabIndex = 1;
        var historyLoad = model.LoadSelectedTabAsync();
        await handler.HistoryRequestStarted.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        model.SelectedTabIndex = 2;
        var templatesLoad = model.LoadSelectedTabAsync();
        handler.ReleaseHistoryResponse();
        await Task.WhenAll(historyLoad, templatesLoad);

        Assert.Contains("/api/v1/admin/site-notifications/history?page=1&pageSize=20", handler.Requests);
        Assert.Contains("/api/v1/admin/site-notifications/templates", handler.Requests);
        Assert.Equal(2, model.SelectedTabIndex);
        Assert.Single(model.Templates);
        await model.DeactivateAsync(true);
    }

    private static async Task<AdminSessionClient> CreateClientAsync(SiteNotificationHandler handler)
    {
        var client = new AdminSessionClient(new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") });
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);

        return client;
    }

    private static async Task WaitForAsync(Func<bool> condition, string failureMessage = "Условие не выполнено вовремя.")
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        if (!condition())
        {
            throw new Xunit.Sdk.XunitException(failureMessage);
        }
    }

    private sealed class SiteNotificationHandler : HttpMessageHandler
    {
        private static readonly DateTime s_now = new(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        private readonly TaskCompletionSource _historyRequestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseHistoryResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Requests { get; } = [];
        public bool FailNextSend { get; set; }
        public bool LegacyMode { get; init; }
        public bool HoldHistoryResponse { get; init; }
        public Task HistoryRequestStarted => this._historyRequestStarted.Task;

        public void ReleaseHistoryResponse() => this._releaseHistoryResponse.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
            {
                return SiteNotificationHandler.Json(new
                {
                    accessToken = "access-token", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                    refreshToken = "refresh", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    user = new { id = "admin", email = "contact@example.invalid", roles = Array.Empty<string>() }
                });
            }

            if (path == "/api/v1/admin/current-user")
            {
                return SiteNotificationHandler.Json(new AdminProfile
                {
                    Id = "admin", IsSystem = true,
                    Permissions = ["Admin.Access", "Admin.Communications.View", "Admin.Communications.Manage"]
                });
            }

            var pathAndQuery = request.RequestUri.PathAndQuery;
            this.Requests.Add(pathAndQuery);

            if (path == "/api/v1/admin/site-notifications/users")
            {
                return SiteNotificationHandler.Json(new[] { new AdminSiteNotificationUser { Id = "user-1", Email = "contact@example.invalid" } });
            }

            if (path == "/api/v1/admin/site-notifications/send" && this.FailNextSend)
            {
                this.FailNextSend = false;
                throw new HttpRequestException("Ответ потерян после отправки.");
            }

            if (path == "/api/v1/admin/site-notifications/history")
            {
                if (this.LegacyMode)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                if (this.HoldHistoryResponse)
                {
                    this._historyRequestStarted.TrySetResult();
                    await this._releaseHistoryResponse.Task.WaitAsync(cancellationToken);
                }

                var requestedPage = int.Parse(request.RequestUri.Query.TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Single(value => value.StartsWith("page=", StringComparison.Ordinal))["page=".Length..]);

                return SiteNotificationHandler.Json(new AdminSiteNotificationHistoryPage
                {
                    Page = requestedPage,
                    PageSize = 20,
                    TotalCount = 25,
                    Items = Enumerable.Range(0, 20).Select(index => new AdminSiteNotification
                    {
                        Id = Guid.NewGuid(), Title = $"Историческое уведомление {index}", Body = "Текст",
                        CreatedAt = SiteNotificationHandler.s_now
                    }).ToArray()
                });
            }

            if (path == "/api/v1/admin/site-notifications/templates")
            {
                if (this.LegacyMode)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                return SiteNotificationHandler.Json(new[] { new AdminSiteNotification { IsTemplate = true, TypeCode = "welcome" } });
            }

            if (path == "/api/v1/admin/site-notifications" && this.LegacyMode)
            {
                var items = Enumerable.Range(0, 12)
                    .Select(index => new AdminSiteNotification
                    {
                        Id = Guid.Parse($"00000000-0000-0000-0000-{index + 1:000000000000}"),
                        Title = $"История {index}",
                        CreatedAt = SiteNotificationHandler.s_now.AddMinutes(-index)
                    })
                    .Append(new AdminSiteNotification { IsTemplate = true, TypeCode = "welcome" })
                    .ToArray();
                return SiteNotificationHandler.Json(items);
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
