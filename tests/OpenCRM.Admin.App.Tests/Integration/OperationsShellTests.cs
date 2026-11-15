using System.Net;
using System.Net.Http.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class OperationsShellTests
{
    [AvaloniaFact]
    public async Task AllOperationsUseTheirViewsAndClearOnPermissionRevocation()
    {
        using var handler = new OperationsHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var client = new AdminSessionClient(http);
        var model = new MainWindowViewModel(client, http.BaseAddress) { Email = "contact@example.invalid", Password ="" };
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            var sections = model.VisibleOperationSections.ToArray();
            Assert.Equal(8, sections.Length);
            Assert.DoesNotContain(sections, section => section.Model is EmailComposeViewModel);
            foreach (var section in sections)
            {
                await model.ShowOperationCommand.ExecuteAsync(section);
                Dispatcher.UIThread.RunJobs();
                Assert.Same(section.Model, model.CurrentOperation);
                Assert.True(section.IsSelected);
                Assert.True(model.IsOperationsOpen);
                Assert.Equal(section.Title, model.PageLabel);
                Assert.Empty(section.Model.ErrorMessage);
                Assert.Contains(window.GetVisualDescendants().OfType<UserControl>(), view =>
                    ReferenceEquals(view.DataContext, section.Model) && view.IsEffectivelyVisible);
            }

            var campaigns = sections.Single(section => section.Model is EmailCampaignsViewModel);
            await model.ShowOperationCommand.ExecuteAsync(campaigns);
            ((EmailCampaignsViewModel)campaigns.Model).ComposeCommand.Execute(null);
            await model.ShowOperationCommand.ExecutionTask!;
            Assert.IsType<EmailComposeViewModel>(model.CurrentOperation);
            Assert.True(campaigns.IsSelected);

            handler.IsSystem = false;
            await model.RefreshProfileCommand.ExecuteAsync(null);
            Assert.Empty(model.VisibleOperationSections);
            Assert.Null(model.CurrentOperation);
            Assert.True(model.IsProfileOpen);
            var count = handler.OperationRequests;
            await model.ShowOperationCommand.ExecuteAsync(sections[0]);
            Assert.Equal(count, handler.OperationRequests);
        }
        finally
        {
            await model.ShutdownAsync();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task OperationNavigationRemainsEnabledDuringLoading()
    {
        using var handler = new OperationsHandler { HoldCampaigns = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var client = new AdminSessionClient(http);
        var model = new MainWindowViewModel(client, http.BaseAddress) { Email = "contact@example.invalid", Password ="" };
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            var sections = model.VisibleOperationSections.ToArray();
            var campaigns = sections.Single(section => section.Model is EmailCampaignsViewModel);
            var catalog = sections.Single(section => section.Model is VehicleCatalogViewModel);
            var firstNavigation = model.ShowOperationCommand.ExecuteAsync(campaigns);
            await handler.CampaignsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            var buttons = window.GetVisualDescendants().OfType<Button>()
                .Where(button => ReferenceEquals(button.Command, model.ShowOperationCommand)).ToArray();
            Assert.Equal(sections.Length, buttons.Length);
            Assert.All(buttons, button => Assert.True(button.IsEffectivelyEnabled));
            Assert.True(model.ShowOperationCommand.CanExecute(catalog));
            await model.ShowOperationCommand.ExecuteAsync(catalog);
            await firstNavigation;
            Assert.Same(catalog.Model, model.CurrentOperation);
            Assert.True(catalog.IsSelected);
            Assert.False(campaigns.IsSelected);
            Assert.Empty(catalog.Model.ErrorMessage);
        }
        finally
        {
            await model.ShutdownAsync();
            window.Close();
        }
    }

    private sealed class OperationsHandler : HttpMessageHandler
    {
        public bool HoldCampaigns { get; init; }
        public TaskCompletionSource CampaignsStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsSystem { get; set; } = true;
        public int OperationRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path is "/api/v1/auth/login" or "/api/v1/auth/refresh")
            {
                return Reply(new { accessToken = "access-token", refreshToken = "refresh", tokenType = "Bearer",
                    accessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1), refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    user = new { id = "admin" } });
            }

            if (path == "/api/v1/admin/current-user")
            {
                return Reply(new AdminProfile { Id = "admin", IsSystem = this.IsSystem, Permissions = ["Admin.Access"] });
            }

            if (path == "/api/v1/auth/revoke")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            if (path == "/api/v1/admin/navigation/counts")
            {
                return Reply(new AdminNavigationCounts());
            }

            if (path.Contains("dashboard", StringComparison.Ordinal))
            {
                return Reply(new AdminDashboard());
            }

            this.OperationRequests++;
            if (path == "/api/v1/admin/assistant")
            {
                return Reply(new AdminAssistantOverview());
            }

            if (path == "/api/v1/admin/assistant/usage")
            {
                return Reply(new AdminAssistantUsageResponse());
            }
            if (path.EndsWith("/test-data/organizations/page", StringComparison.Ordinal))
            {
                return Reply(new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 20 });
            }
            if (path.EndsWith("/compose-data", StringComparison.Ordinal))
            {
                return Reply(new { users = Array.Empty<object>(), organizationTypes = Array.Empty<object>() });
            }

            if (path.EndsWith("/campaigns", StringComparison.Ordinal))
            {
                if (this.HoldCampaigns)
                {
                    this.CampaignsStarted.TrySetResult();
                    return OperationsHandler.WaitForCancellationAsync(cancellationToken);
                }

                return Reply(new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 20 });
            }

            if (path.EndsWith("/sms-sending-histories", StringComparison.Ordinal))
            {
                return Reply(new { histories = Array.Empty<object>(), totalCount = 0 });
            }

            if (path.EndsWith("/site-notifications/history", StringComparison.Ordinal))
            {
                return Reply(new AdminSiteNotificationHistoryPage { Page = 1, PageSize = 20 });
            }

            return Reply(Array.Empty<object>());
        }

        private static async Task<HttpResponseMessage> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        private static Task<HttpResponseMessage> Reply<T>(T value) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value)
        });
    }
}
