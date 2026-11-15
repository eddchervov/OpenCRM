using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class UserManagementViewTests
{
    [Fact]
    public async Task ImpersonationUsesSelectedUserAndKeepsAdministratorSession()
    {
        using var handler = new ManagementViewHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new UsersViewModel(client);
        await model.ActivateAsync(true);
        await model.OpenCardCommand.ExecuteAsync(model.Rows[0]);
        var managementTab = model.Card!.Tabs.Single(tab => tab.IsManagement);
        Assert.All(model.Management.Targets.Where(target => target.Scope == "organizations"),
            target => Assert.Equal("organizations", target.Scope));
        Assert.Contains(model.Management.Targets, target => target.Scope == "users"
            && target.Name == "Личный доступ пользователя" && target.TargetId == "target-user");
        model.Card.SelectedTab = managementTab;
        Assert.Same(managementTab, model.Card.SelectedTab);
        string? impersonatedUserId = null;
        model.Management.ImpersonationRequested += (userId, _) =>
        {
            impersonatedUserId = userId;

            return Task.CompletedTask;
        };

        await model.Management.BeginActionCommand.ExecuteAsync("impersonate");
        Assert.Contains("contact@example.invalid", model.Management.ConfirmationText);
        Assert.False(model.Management.NeedsReason);
        await model.Management.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal("target-user", impersonatedUserId);
        Assert.Equal("admin", client.CurrentProfile!.Id);
        Assert.True(client.HasSession);
        Assert.Empty(handler.Mutations);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task PersonalAccessUsesUserRoutesAndPersonalPlans()
    {
        using var handler = new ManagementViewHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new UsersViewModel(client);
        await model.ActivateAsync(true);
        await model.OpenCardCommand.ExecuteAsync(model.Rows[0]);
        model.SetBillingAccess(true);

        Assert.Equal("organizations", model.Management.SelectedTarget!.Scope);
        var personalTarget = model.Management.Targets.Single(target => target.Scope == "users");
        Assert.Equal("Личный доступ пользователя", personalTarget.Name);
        Assert.Equal("target-user", personalTarget.TargetId);
        model.Management.SelectedTarget = personalTarget;

        await model.Management.BeginActionCommand.ExecuteAsync("grant-plan");

        Assert.Equal("personal", Assert.Single(model.Management.TargetPlans).PlanTypeCode);
        model.Management.Reason = "Выдача личного тарифа по обращению";
        model.Management.Days = 30;
        await model.Management.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal("/api/v1/admin/billing/users/target-user/plan", Assert.Single(handler.Mutations).Path);
        Assert.True(model.Management.CanExtend);
        Assert.Equal("users", model.Management.SelectedTarget!.Scope);

        await model.Management.BeginActionCommand.ExecuteAsync("extend");
        model.Management.Reason = "Продление личного тарифа";
        model.Management.Days = 7;
        await model.Management.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal("/api/v1/admin/billing/subscriptions/users/401/extend", handler.Mutations[1].Path);
        using (var request = JsonDocument.Parse(handler.Mutations[1].Body))
        {
            Assert.Equal(ManagementViewHandler.UserEndDate,
                request.RootElement.GetProperty("expectedEndDate").GetDateTime());
        }

        await model.Management.BeginActionCommand.ExecuteAsync("stop");
        Assert.True(model.Management.IsConfirmationOpen);
        model.Management.Reason = "Остановка личного доступа по обращению";
        Assert.True(model.Management.ConfirmCommand.CanExecute(null));
        await model.Management.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal("/api/v1/admin/billing/subscriptions/users/401/stop", handler.Mutations[2].Path);
        await model.DeactivateAsync(true);
        Assert.Equal(3, handler.Mutations.Count);
    }

    [Fact]
    public void PersonalTargetExistsWhenUserHasNoOrganizations()
    {
        using var http = new HttpClient(new ManagementViewHandler()) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        var model = new UserManagementViewModel(client);

        model.SetOverview(new AdminUserOverview
        {
            Profile = new AdminUserOverviewProfile { Id = "target-user", Email = "contact@example.invalid" }
        }, canViewBilling: true);

        var target = Assert.Single(model.Targets);
        Assert.Equal("users", target.Scope);
        Assert.Equal("Личный доступ пользователя", target.Name);
        Assert.Equal("target-user", target.TargetId);
        Assert.True(target.IsAvailable);
    }

    [AvaloniaFact]
    public async Task ManagementIsHiddenOnOverviewAndOrganizationChangesRefreshTheSameCard()
    {
        using var handler = new ManagementViewHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new UsersViewModel(client);
        await model.ActivateAsync(true);
        await model.OpenCardCommand.ExecuteAsync(model.Rows[0]);
        var window = new Window { Width = 1280, Height = 1100, Content = new UsersView { DataContext = model } };
        window.Show();
        try
        {
            var managementTab = model.Card!.Tabs.Single(tab => tab.IsManagement);
            Assert.True(model.Card.Tabs[0].IsOverview);
            Assert.NotSame(managementTab, model.Card.SelectedTab);
            Dispatcher.UIThread.RunJobs();
            var managementView = window.GetVisualDescendants().OfType<UserManagementView>().Single();
            Assert.False(managementView.IsEffectivelyVisible);

            model.Card.SelectedTab = managementTab;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(managementTab, model.Card.SelectedTab);
            Assert.True(managementView.IsEffectivelyVisible);
            UserManagementViewTests.SaveScreenshot(window, "user-management-1280.png");

            UserManagementViewTests.Click(window, "UserGrantPlan");
            await model.Management.BeginActionCommand.ExecutionTask!;
            Assert.True(model.Management.IsConfirmationOpen);
            Assert.Equal("business", Assert.Single(model.Management.TargetPlans).PlanTypeCode);
            Assert.False(model.Management.ConfirmCommand.CanExecute(null));
            model.Management.Reason = "Выдача тарифа по обращению";
            model.Management.Days = 30;
            model.SetBillingAccess(true);
            Assert.True(model.Management.IsConfirmationOpen);
            Assert.Equal("Выдача тарифа по обращению", model.Management.Reason);
            Assert.True(model.Management.ConfirmCommand.CanExecute(null));
            Dispatcher.UIThread.RunJobs();
            var planChoice = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "UserPlanChoice");
            Assert.Same(model.Management.SelectedPlan, planChoice.SelectedItem);
            Assert.Contains(planChoice.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Рабочее пространство");
            Assert.Empty(handler.Mutations);
            UserManagementViewTests.SaveScreenshot(window, "user-grant-plan-confirmation.png");
            window.Width = 592;
            Dispatcher.UIThread.RunJobs();
            UserManagementViewTests.SaveScreenshot(window, "user-management-592.png");
            window.Width = 1280;
            Dispatcher.UIThread.RunJobs();
            var previousCard = model.Card;

            UserManagementViewTests.Click(window, "UserManagementConfirm");
            await model.Management.ConfirmCommand.ExecutionTask!;

            Assert.NotSame(previousCard, model.Card);
            Assert.True(model.Card!.SelectedTab!.IsManagement);
            Assert.Equal(2, handler.OverviewRequests);
            Assert.False(model.Management.CanGrantPlan);
            Assert.True(model.Management.CanExtend);
            Assert.Equal($"/api/v1/admin/billing/organizations/{ManagementViewHandler.OrganizationId}/plan", Assert.Single(handler.Mutations).Path);
            Assert.True(model.Management.HasSuccess);

            model.Management.SelectedTarget = model.Management.Targets.Single(target => target.TargetId == ManagementViewHandler.ActiveOrganizationId.ToString());
            UserManagementViewTests.Click(window, "UserExtendPlan");
            await model.Management.BeginActionCommand.ExecutionTask!;
            model.Management.Reason = "Продление студии";
            model.Management.Days = 7;
            UserManagementViewTests.Click(window, "UserManagementConfirm");
            await model.Management.ConfirmCommand.ExecutionTask!;

            Assert.Equal("/api/v1/admin/billing/subscriptions/organizations/202/extend", handler.Mutations[1].Path);
            using var request = JsonDocument.Parse(handler.Mutations[1].Body);
            Assert.Equal(7, request.RootElement.GetProperty("days").GetInt32());
            Assert.Equal(ManagementViewHandler.OrganizationEndDate,
                request.RootElement.GetProperty("expectedEndDate").GetDateTime());
            Assert.Equal("organizations", model.Management.SelectedTarget!.Scope);
            Assert.Equal(3, handler.OverviewRequests);
            Assert.True(model.Card.SelectedTab!.IsManagement);

            await model.Management.BeginActionCommand.ExecuteAsync("test-subscriptions");
            Assert.Contains("contact@example.invalid", model.Management.ConfirmationText);
            Assert.DoesNotContain("Детейлинг-студия", model.Management.ConfirmationText);
            model.Management.CancelCommand.Execute(null);

            await model.Management.BeginActionCommand.ExecuteAsync("stop");
            Assert.Contains("Все", model.Management.ConfirmationText);
            await model.BackToListCommand.ExecuteAsync(null);
            model.SetBillingAccess(false);
            Assert.False(model.Management.IsConfirmationOpen);
            Assert.False(model.Management.IsVisible);
            Assert.Equal(2, handler.Mutations.Count);
        }
        finally
        {
            await model.DeactivateAsync(true);
            window.Close();
        }
    }

    private static void Click(Window window, string id)
    {
        Dispatcher.UIThread.RunJobs();
        var button = window.GetVisualDescendants().OfType<Button>()
            .Single(control => AutomationProperties.GetAutomationId(control) == id);
        button.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        Assert.True(button.IsEffectivelyVisible);
        Assert.True(button.IsEffectivelyEnabled);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

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

    private sealed class ManagementViewHandler : HttpMessageHandler
    {
        public static readonly DateTime OrganizationEndDate = new(2026, 12, 1, 12, 30, 0, DateTimeKind.Utc);
        public static readonly DateTime UserEndDate = new(2026, 12, 15, 9, 0, 0, DateTimeKind.Utc);
        public static readonly Guid OrganizationId = Guid.Parse("2af7d26c-9976-470f-8fa7-93d3f56aa9d0");
        public static readonly Guid ActiveOrganizationId = Guid.Parse("f8e3c435-0795-4af2-95ee-fb08f6c462b8");
        private bool _hasOrganizationPlan;
        private bool _hasUserPlan;
        public int OverviewRequests { get; private set; }
        public List<(string Path, string Body)> Mutations { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path is "/api/v1/auth/login" or "/api/v1/auth/refresh")
            {
                return ManagementViewHandler.Json(new
                {
                    accessToken = "access-token", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
                    refreshToken = "refresh", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    user = new { id = "admin", email = "contact@example.invalid" }
                });
            }

            if (path == "/api/v1/admin/current-user")
            {
                return ManagementViewHandler.Json(new AdminProfile { Id = "admin", IsSystem = true });
            }

            if (path == "/api/v1/admin/users")
            {
                return ManagementViewHandler.Json(new AdminUsersResponse
                {
                    AllCount = 1, Page = 1, PageSize = 50,
                    Users = [new AdminUserListItem { Id = "target-user", Email = "contact@example.invalid" }]
                });
            }

            if (path.EndsWith("/overview", StringComparison.Ordinal))
            {
                this.OverviewRequests++;
                var organizationSubscriptions = new List<AdminOrganizationSubscription>
                {
                    new()
                    {
                        Id = 202, OrganizationId = ManagementViewHandler.ActiveOrganizationId,
                        PlanName = "Рабочее пространство", IsActive = true, EndDate = ManagementViewHandler.OrganizationEndDate
                    }
                };
                if (this._hasOrganizationPlan)
                {
                    organizationSubscriptions.Insert(0, new AdminOrganizationSubscription
                    {
                        Id = 303, OrganizationId = ManagementViewHandler.OrganizationId, IsActive = true,
                        PlanName = "Рабочее пространство", EndDate = DateTime.UtcNow.AddDays(30)
                    });
                }

                return ManagementViewHandler.Json(new AdminUserOverview
                {
                    Profile = new AdminUserOverviewProfile { Id = "target-user", Email = "contact@example.invalid" },
                    Organizations = [new AdminUserOverviewOrganization
                    {
                        OrganizationId = ManagementViewHandler.OrganizationId, OrganizationName = "Детейлинг-студия",
                        IsActive = true, HasActiveSubscription = this._hasOrganizationPlan,
                        CurrentSubscriptionId = this._hasOrganizationPlan ? 303 : null,
                        CurrentPlanName = this._hasOrganizationPlan ? "Рабочее пространство" : string.Empty,
                        CurrentPlanEndDate = this._hasOrganizationPlan ? DateTime.UtcNow.AddDays(30) : null
                    }, new AdminUserOverviewOrganization
                    {
                        OrganizationId = ManagementViewHandler.ActiveOrganizationId, OrganizationName = "СТО",
                        IsActive = true, HasActiveSubscription = true, CurrentSubscriptionId = 202,
                        CurrentPlanName = "Рабочее пространство", CurrentPlanEndDate = ManagementViewHandler.OrganizationEndDate
                    }],
                    Subscriptions = new AdminUserOverviewSubscriptions
                    {
                        Organizations = organizationSubscriptions,
                        Users = this._hasUserPlan ?
                        [
                            new AdminUserSubscription
                            {
                                Id = 401, PlanName = "Личный", PlanTypeCode = "personal", IsActive = true,
                                StartDate = ManagementViewHandler.UserEndDate.AddDays(-30), EndDate = ManagementViewHandler.UserEndDate
                            }
                        ] : []
                    }
                });
            }

            if (path == "/api/v1/admin/billing/available-plans")
            {
                return ManagementViewHandler.Json(new[]
                {
                    new AdminAvailablePlan { Id = 10, Name = "Личный", PlanTypeCode = "personal", DurationDays = 30 },
                    new AdminAvailablePlan { Id = 20, Name = "Рабочее пространство", PlanTypeCode = "business", DurationDays = 30 }
                });
            }

            if (request.Method == HttpMethod.Post && path.StartsWith("/api/v1/admin/billing/", StringComparison.Ordinal))
            {
                this.Mutations.Add((path, await request.Content!.ReadAsStringAsync(cancellationToken)));
                if (path.StartsWith("/api/v1/admin/billing/users/", StringComparison.Ordinal))
                {
                    this._hasUserPlan = true;
                }
                else
                {
                    this._hasOrganizationPlan = true;
                }

                return ManagementViewHandler.Json(new AdminOperationResponse { Message = "Изменения сохранены." });
            }

            throw new InvalidOperationException("Неожиданный запрос: " + path);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
