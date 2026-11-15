using System.Net;
using System.Net.Http.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class ConfigurationShellTests
{
    [AvaloniaFact]
    public async Task ConfigurationScreensUseDesktopNavigationAndClearWhenPermissionIsRevoked()
    {
        var handler = new ConfigurationHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        var model = new MainWindowViewModel(client, http.BaseAddress) { Email = "contact@example.invalid", Password ="" };
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(FindNavigation(window, "SubscriptionPlansNavigation").IsEffectivelyVisible);
            Assert.True(FindNavigation(window, "PromoCodesNavigation").IsEffectivelyVisible);
            Assert.Contains(model.NavigationItems, item => item.Title == "Тарифы" && !item.IsPlaceholder);
            Assert.Contains(model.NavigationItems, item => item.Title == "Промокоды" && !item.IsPlaceholder);
            await model.ShowSubscriptionPlansCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsSubscriptionPlansOpen);
            Assert.Equal("Тарифы", model.PageLabel);
            Assert.True(window.GetVisualDescendants().OfType<SubscriptionPlansView>().Single().IsEffectivelyVisible);
            Assert.True(handler.PlanRequests > 0);
            await model.ShowPromoCodesCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsPromoCodesOpen);
            Assert.False(model.IsSubscriptionPlansOpen);
            Assert.True(window.GetVisualDescendants().OfType<PromoCodesView>().Single().IsEffectivelyVisible);
            Assert.Equal(1, handler.PromoRequests);

            handler.CanConfigure = false;
            await model.RefreshProfileCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.CanManageConfiguration);
            Assert.False(model.IsPromoCodesOpen);
            Assert.True(model.IsProfileOpen);
            Assert.False(FindNavigation(window, "PromoCodesNavigation").IsEffectivelyVisible);
            var requests = handler.PlanRequests;
            await model.ShowSubscriptionPlansCommand.ExecuteAsync(null);
            await model.ShowPromoCodesCommand.ExecuteAsync(null);
            Assert.Equal(requests, handler.PlanRequests);
            Assert.Equal(1, handler.PromoRequests);
        }
        finally
        {
            await model.ShutdownAsync();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task NeutralModeHidesCommercialNavigationAndDoesNotRequestPlansOrPromocodes()
    {
        var handler = new ConfigurationHandler { CommercialEnabled = false };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        var model = new MainWindowViewModel(client, http.BaseAddress) { Email = "contact@example.invalid", Password = "" };
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            Assert.False(model.CanViewBilling);
            Assert.False(model.CanManageCommercialConfiguration);
            Assert.DoesNotContain(model.NavigationItems, item => item.Title is "Платежи и подписки" or "Тарифы" or "Промокоды");
            await model.ShowSubscriptionPlansCommand.ExecuteAsync(null);
            await model.ShowPromoCodesCommand.ExecuteAsync(null);
            Assert.Equal(0, handler.PlanRequests);
            Assert.Equal(0, handler.PromoRequests);
            Assert.True(model.IsProfileOpen);
        }
        finally
        {
            await model.ShutdownAsync();
        }
    }

    [AvaloniaFact]
    public async Task AdministratorWithoutConfigurationPermissionCannotLoadEitherScreen()
    {
        var handler = new ConfigurationHandler { CanConfigure = false };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        var model = new MainWindowViewModel(client, http.BaseAddress) { Email = "contact@example.invalid", Password ="" };
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            await model.ShowSubscriptionPlansCommand.ExecuteAsync(null);
            await model.ShowPromoCodesCommand.ExecuteAsync(null);
            Assert.False(model.CanManageConfiguration);
            Assert.True(model.IsProfileOpen);
            Assert.Equal(0, handler.PlanRequests);
            Assert.Equal(0, handler.PromoRequests);
        }
        finally
        {
            await model.ShutdownAsync();
        }
    }

    [AvaloniaFact]
    public async Task LeavingConfigurationCancelsPendingLoadAndKeepsProfileOpen()
    {
        var handler = new ConfigurationHandler { BlockPlanLoad = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        var model = new MainWindowViewModel(client, http.BaseAddress) { Email = "contact@example.invalid", Password ="" };
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            var loading = model.ShowSubscriptionPlansCommand.ExecuteAsync(null);
            await handler.PlanLoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await model.ShowProfileCommand.ExecuteAsync(null);
            await loading;
            Assert.True(model.IsProfileOpen);
            Assert.False(model.IsSubscriptionPlansOpen);
            Assert.Equal("Профиль", model.PageLabel);
        }
        finally
        {
            await model.ShutdownAsync();
        }
    }

    [AvaloniaFact]
    public async Task FirstPlanCanBeCreatedWhenCatalogIsEmpty()
    {
        var handler = new ConfigurationHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        var model = new MainWindowViewModel(client, http.BaseAddress) { Email = "contact@example.invalid", Password ="" };
        var window = new MainWindow { DataContext = model, Width = 900, Height = 800 };
        window.Show();
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            await model.ShowSubscriptionPlansCommand.ExecuteAsync(null);
            model.SubscriptionPlans.CreatePlanCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var name = window.GetVisualDescendants().OfType<TextBox>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "PlanName");
            Assert.True(name.IsEffectivelyVisible);
            Assert.True(name.IsEffectivelyEnabled);
            Assert.True(model.SubscriptionPlans.IsEditorOpen);
            Assert.True(model.SubscriptionPlans.IsCreating);
        }
        finally
        {
            await model.ShutdownAsync();
            window.Close();
        }
    }

    private static Button FindNavigation(Window window, string id) => window.GetVisualDescendants()
        .OfType<Button>().Single(control => AutomationProperties.GetAutomationId(control) == id);

    private sealed class ConfigurationHandler : HttpMessageHandler
    {
        public bool CanConfigure { get; set; } = true;

        public bool CommercialEnabled { get; set; } = true;

        public bool BlockPlanLoad { get; init; }

        public TaskCompletionSource PlanLoadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int PlanRequests { get; private set; }

        public int PromoRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path is "/api/v1/auth/login" or "/api/v1/auth/refresh")
            {
                return Reply(new
                {
                    accessToken = "access-token", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                    refreshToken = "refresh", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    user = new { id = "administrator", email = "contact@example.invalid" }
                });
            }

            if (path == "/api/v1/admin/current-user")
            {
                return Reply(new AdminProfile
                {
                    Id = "administrator", Email = "contact@example.invalid", Roles = ["Administrator"],
                    CommercialEnabled = this.CommercialEnabled,
                    Permissions = this.CanConfigure ? ["Admin.Access", "Admin.Configuration.Manage"] : ["Admin.Access"]
                });
            }

            if (path == "/api/v1/admin/subscription-plans")
            {
                this.PlanRequests++;
                this.PlanLoadStarted.TrySetResult();
                if (this.BlockPlanLoad)
                {
                    return WaitForCancellationAsync(cancellationToken);
                }

                return Reply(Array.Empty<object>());
            }

            if (path == "/api/v1/admin/promo-codes")
            {
                this.PromoRequests++;

                return Reply(Array.Empty<object>());
            }

            return Task.FromResult(new HttpResponseMessage(path == "/api/v1/auth/revoke"
                ? HttpStatusCode.NoContent : HttpStatusCode.NotFound));
        }

        private static async Task<HttpResponseMessage> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            throw new InvalidOperationException("Загрузка должна быть отменена при выходе из раздела.");
        }

        private static Task<HttpResponseMessage> Reply<T>(T body) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(body)
        });
    }
}
