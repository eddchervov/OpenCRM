using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class UserManagementTests
{
    [Fact]
    public async Task AccessFlagsRespectPermissionsDeletionAndCurrentUser()
    {
        var handler = new UserManagementApiHandler("Admin.Users.Manage", "Admin.Billing.Manage", "Admin.Users.Impersonate");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        await UserManagementTests.LoginAsync(session);
        var model = new UserManagementViewModel(session);

        model.SetOverview(UserManagementTests.CreateOverview("another-user"), true);

        Assert.Contains(model.Targets, target => target.Scope == "organizations");
        Assert.Equal("another-user", Assert.Single(model.Targets, target => target.Scope == "users").TargetId);
        Assert.Equal(model.Targets[0], model.SelectedTarget);
        Assert.True(model.CanManageTests);
        Assert.True(model.CanManageBilling);
        Assert.True(model.CanImpersonate);
        Assert.True(model.IsVisible);
        Assert.Equal("Выключены", model.TestSubscriptionsLabel);
        model.SetOverview(UserManagementTests.CreateOverview("admin"), true);
        Assert.False(model.CanImpersonate);
        model.SetOverview(UserManagementTests.CreateOverview("deleted-user", isDeleted: true), true);
        Assert.False(model.CanImpersonate);
        Assert.False(model.CanManageTests);
        Assert.False(model.CanManageBilling);
        Assert.False(model.IsVisible);
    }

    [Fact]
    public async Task GrantPlanUsesSelectedOrganizationAndTrimmedReason()
    {
        var handler = new UserManagementApiHandler("Admin.Billing.Manage");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        await UserManagementTests.LoginAsync(session);
        var model = new UserManagementViewModel(session);
        var organizationId = Guid.NewGuid();
        model.SetOverview(UserManagementTests.CreateOverview("target-user", organizationId), true);

        await model.BeginActionCommand.ExecuteAsync("grant-plan");
        Assert.True(model.IsConfirmationOpen);
        Assert.Equal("business", model.SelectedPlan!.PlanTypeCode);
        model.Days = 45;
        model.Reason = "  компенсация  ";
        await model.ConfirmCommand.ExecuteAsync(null);

        var request = Assert.Single(handler.Mutations);
        Assert.Equal($"/api/v1/admin/billing/organizations/{organizationId}/plan", request.Path);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(11, body.RootElement.GetProperty("planId").GetInt32());
        Assert.Equal(45, body.RootElement.GetProperty("days").GetInt32());
        Assert.Equal("компенсация", body.RootElement.GetProperty("reason").GetString());
        Assert.Contains("выполнена на сервере", model.SuccessMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(model.BeginActionCommand.CanExecute("grant-plan"));
    }

    [Fact]
    public async Task TargetsContainOrganizationsAndPersonalAccessAndDefaultToTheFirstOrganization()
    {
        var handler = new UserManagementApiHandler("Admin.Billing.Manage");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        await UserManagementTests.LoginAsync(session);
        var model = new UserManagementViewModel(session);
        var firstOrganizationId = Guid.Parse("02a0a9f4-7171-4fc9-9c54-a7785eec501e");
        var secondOrganizationId = Guid.Parse("acc17fc3-8a34-46cc-a520-0871503b3160");
        model.SetOverview(UserManagementTests.CreateOverview(
            "target-user",
            firstOrganizationId,
            secondOrganizationId: secondOrganizationId), true);

        Assert.Equal(3, model.Targets.Count);
        Assert.Equal(new[] { firstOrganizationId.ToString(), secondOrganizationId.ToString() },
            model.Targets.Where(target => target.Scope == "organizations").Select(target => target.TargetId));
        Assert.Equal("target-user", Assert.Single(model.Targets, target => target.Scope == "users").TargetId);
        Assert.Equal(firstOrganizationId.ToString(), model.SelectedTarget!.TargetId);
    }

    [Fact]
    public async Task ExtendSendsExactExpectedEndDateFromOverview()
    {
        var handler = new UserManagementApiHandler("Admin.Billing.Manage");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        await UserManagementTests.LoginAsync(session);
        var model = new UserManagementViewModel(session);
        var expectedEndDate = new DateTime(2026, 12, 4, 8, 30, 0, DateTimeKind.Utc);
        model.SetOverview(UserManagementTests.CreateOverview("target-user", organizationEnd: expectedEndDate), true);

        await model.BeginActionCommand.ExecuteAsync("extend");
        model.Days = 20;
        model.Reason = "Исправление срока";
        await model.ConfirmCommand.ExecuteAsync(null);

        var request = Assert.Single(handler.Mutations);
        Assert.Equal("/api/v1/admin/billing/subscriptions/organizations/17/extend", request.Path);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(expectedEndDate, body.RootElement.GetProperty("expectedEndDate").GetDateTime());
        Assert.Equal(20, body.RootElement.GetProperty("days").GetInt32());
    }

    [Fact]
    public async Task NoOrganizationsKeepsPersonalBillingTargetAndTestSubscriptionsAvailable()
    {
        var handler = new UserManagementApiHandler("Admin.Users.Manage", "Admin.Billing.Manage");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        await UserManagementTests.LoginAsync(session);
        var model = new UserManagementViewModel(session);
        model.SetOverview(UserManagementTests.CreateOverview("target-user", includeOrganization: false), true);

        var personalTarget = Assert.Single(model.Targets);
        Assert.Equal("users", personalTarget.Scope);
        Assert.Equal("target-user", personalTarget.TargetId);
        Assert.Equal(personalTarget, model.SelectedTarget);
        Assert.True(model.CanGrantDemo);
        Assert.True(model.CanGrantPlan);
        Assert.False(model.CanExtend);
        Assert.False(model.CanStop);
        Assert.True(model.CanManageTests);

        await model.BeginActionCommand.ExecuteAsync("test-subscriptions");
        await model.ConfirmCommand.ExecuteAsync(null);

        var request = Assert.Single(handler.Mutations);
        Assert.Equal("/api/v1/admin/users/target-user/test-subscriptions", request.Path);
    }

    [Theory]
    [InlineData("detailing")]
    [InlineData("service-station")]
    public async Task CurrentOrganizationScenariosUseFreeAndCannotReceiveManualDemo(string scenarioCode)
    {
        var handler = new UserManagementApiHandler("Admin.Billing.Manage");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        await UserManagementTests.LoginAsync(session);
        var model = new UserManagementViewModel(session);
        model.SetOverview(UserManagementTests.CreateOverview("target-user", scenarioCode: scenarioCode), true);

        Assert.False(model.CanGrantDemo);
        Assert.Contains("Free · без ограничения срока", model.CurrentTargetSummary, StringComparison.Ordinal);
        Assert.False(model.BeginActionCommand.CanExecute("grant-demo"));
    }

    [Fact]
    public async Task StoppingCurrentOrganizationSubscriptionExplainsFreeFallback()
    {
        var handler = new UserManagementApiHandler("Admin.Billing.Manage");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        await UserManagementTests.LoginAsync(session);
        var model = new UserManagementViewModel(session);
        model.SetOverview(UserManagementTests.CreateOverview("target-user",
            organizationEnd: DateTime.UtcNow.AddDays(10), scenarioCode: "detailing"), true);

        await model.BeginActionCommand.ExecuteAsync("stop");

        Assert.Contains("останется бесплатный тариф Free", model.ConfirmationText, StringComparison.Ordinal);
        Assert.Contains("история и данные сохранятся", model.ConfirmationText, StringComparison.Ordinal);
        Assert.DoesNotContain("только для чтения", model.ConfirmationText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestSubscriptionAndImpersonationActionsUseTheirOwnPermissionAndTarget()
    {
        var handler = new UserManagementApiHandler("Admin.Users.Manage", "Admin.Users.Impersonate");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        await UserManagementTests.LoginAsync(session);
        var model = new UserManagementViewModel(session);
        model.SetOverview(UserManagementTests.CreateOverview("target-user"), false);

        await model.BeginActionCommand.ExecuteAsync("test-subscriptions");
        await model.ConfirmCommand.ExecuteAsync(null);

        var request = Assert.Single(handler.Mutations);
        Assert.Equal("/api/v1/admin/users/target-user/test-subscriptions", request.Path);
        using (var body = JsonDocument.Parse(request.Body))
        {
            Assert.True(body.RootElement.GetProperty("canUseTestSubscriptions").GetBoolean());
        }

        model.SetOverview(UserManagementTests.CreateOverview("target-user"), false);
        string? impersonatedUserId = null;
        model.ImpersonationRequested += (userId, _) =>
        {
            impersonatedUserId = userId;
            return Task.CompletedTask;
        };
        await model.BeginActionCommand.ExecuteAsync("impersonate");
        Assert.Contains("Войдите в браузере как администратор", model.ConfirmationText, StringComparison.Ordinal);
        Assert.Contains("не передаётся", model.ConfirmationText, StringComparison.Ordinal);
        await model.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal("target-user", impersonatedUserId);
        Assert.Single(handler.Mutations);
    }

    [Fact]
    public async Task ImpersonationBrowserTimeoutIsShownInCardInsteadOfEscapingCommand()
    {
        var handler = new UserManagementApiHandler("Admin.Users.Impersonate");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        await UserManagementTests.LoginAsync(session);
        var model = new UserManagementViewModel(session);
        model.SetOverview(UserManagementTests.CreateOverview("target-user"), false);
        model.ImpersonationRequested += (_, _) => throw new TimeoutException("Браузер не открыл страницу входа.");

        await model.BeginActionCommand.ExecuteAsync("impersonate");
        await model.ConfirmCommand.ExecuteAsync(null);

        Assert.Contains("Не удалось открыть сайт в браузере", model.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("Браузер не открыл страницу входа", model.ErrorMessage, StringComparison.Ordinal);
        Assert.False(model.IsBusy);
        Assert.Empty(handler.Mutations);
    }

    [Fact]
    public async Task ChangingOverviewInvalidatesConfirmationAndCancelDoesNotMutate()
    {
        var handler = new UserManagementApiHandler("Admin.Billing.Manage");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        await UserManagementTests.LoginAsync(session);
        var model = new UserManagementViewModel(session);
        model.SetOverview(UserManagementTests.CreateOverview("target-user"), true);

        await model.BeginActionCommand.ExecuteAsync("grant-demo");
        Assert.True(model.IsConfirmationOpen);
        model.SetOverview(UserManagementTests.CreateOverview("target-user"), true);
        Assert.False(model.IsConfirmationOpen);
        await model.BeginActionCommand.ExecuteAsync("grant-demo");
        model.CancelCommand.Execute(null);

        Assert.Empty(handler.Mutations);
        Assert.DoesNotContain(handler.Requests, path => path.EndsWith("/available-plans", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StaleSubscriptionDateRefreshesOverviewAndShowsActionableError()
    {
        var handler = new UserManagementApiHandler("Admin.Billing.Manage") { MutationStatusCode = HttpStatusCode.Conflict };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        await UserManagementTests.LoginAsync(session);
        var model = new UserManagementViewModel(session);
        var expectedEndDate = new DateTime(2026, 12, 4, 8, 30, 0, DateTimeKind.Utc);
        model.SetOverview(UserManagementTests.CreateOverview("target-user", organizationEnd: expectedEndDate), true);
        model.UserChanged += _ =>
        {
            model.SetOverview(UserManagementTests.CreateOverview("target-user",
                organizationEnd: expectedEndDate.AddDays(3)), true);
            return Task.CompletedTask;
        };

        await model.BeginActionCommand.ExecuteAsync("extend");
        model.Days = 20;
        model.Reason = "Продление срока";
        await model.ConfirmCommand.ExecuteAsync(null);

        Assert.Single(handler.Mutations);
        Assert.Contains("Обновите цель", model.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(expectedEndDate.AddDays(3), model.SelectedTarget!.EndDate);
        Assert.False(model.IsConfirmationOpen);
    }

    [Fact]
    public async Task SwitchingUserDuringMutationCancelsLateResponse()
    {
        var handler = new UserManagementApiHandler("Admin.Billing.Manage") { HoldMutation = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        await UserManagementTests.LoginAsync(session);
        var model = new UserManagementViewModel(session);
        model.SetOverview(UserManagementTests.CreateOverview("first-user"), true);
        var secondOrganizationId = Guid.Parse("ac3104b8-9578-4f43-8b01-f18d33616010");
        await model.BeginActionCommand.ExecuteAsync("grant-demo");
        model.Reason = "Причина";
        var operation = model.ConfirmCommand.ExecuteAsync(null);
        await handler.MutationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        model.SetOverview(UserManagementTests.CreateOverview("second-user", secondOrganizationId), true);
        handler.ReleaseMutation.TrySetResult();
        await operation;

        Assert.Empty(model.SuccessMessage);
        Assert.Equal(secondOrganizationId.ToString(), model.Targets[0].TargetId);
        Assert.Equal("organizations", model.Targets[0].Scope);
        Assert.Equal(model.Targets[0], model.SelectedTarget);
    }

    [Fact]
    public async Task DuplicateConfirmAndRevokedRightsCannotRepeatMutation()
    {
        var handler = new UserManagementApiHandler("Admin.Billing.Manage") { HoldMutation = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        await UserManagementTests.LoginAsync(session);
        var model = new UserManagementViewModel(session);
        model.SetOverview(UserManagementTests.CreateOverview("target-user"), true);
        await model.BeginActionCommand.ExecuteAsync("grant-demo");
        model.Reason = "Причина";
        var first = model.ConfirmCommand.ExecuteAsync(null);
        await handler.MutationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await model.ConfirmCommand.ExecuteAsync(null);

        Assert.Single(handler.Mutations);
        handler.ReleaseMutation.TrySetResult();
        await first;
    }

    private static async Task LoginAsync(AdminSessionClient session)
    {
        await session.LoginAsync("contact@example.invalid", "password");
    }

    private static AdminUserOverview CreateOverview(
        string userId,
        Guid? organizationId = null,
        Guid? secondOrganizationId = null,
        DateTime? organizationEnd = null,
        bool includeOrganization = true,
        bool isDeleted = false,
        string scenarioCode = "fleet")
    {
        var orgId = organizationId ?? Guid.NewGuid();
        var organizations = new List<AdminUserOverviewOrganization>
        {
            new()
            {
                OrganizationId = orgId,
                OrganizationName = "Бизнес-центр",
                PrimaryScenarioCode = scenarioCode,
                IsActive = true,
                CurrentPlanName = organizationEnd == null ? string.Empty : "Бизнес",
                HasActiveSubscription = organizationEnd != null,
                CurrentSubscriptionId = organizationEnd == null ? null : 17,
                CurrentPlanEndDate = organizationEnd
            }
        };
        if (secondOrganizationId is { } nextOrganizationId)
        {
            organizations.Add(new AdminUserOverviewOrganization
            {
                OrganizationId = nextOrganizationId,
                OrganizationName = "Второй бизнес-центр",
                IsActive = true
            });
        }

        return new AdminUserOverview
        {
            Profile = new AdminUserOverviewProfile { Id = userId, Email = userId + "@example.com", IsDeleted = isDeleted },
            Organizations = includeOrganization ? organizations : [],
            Subscriptions = new AdminUserOverviewSubscriptions
            {
                Organizations = organizationEnd == null ? [] : [new AdminOrganizationSubscription
                {
                    Id = 17,
                    OrganizationId = orgId,
                    PlanName = "Бизнес",
                    EndDate = organizationEnd.Value,
                    IsActive = true
                }]
            }
        };
    }

    private sealed class UserManagementApiHandler(params string[] permissions) : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);
        private readonly string[] _permissions = permissions;

        public List<string> Requests { get; } = [];

        public List<CapturedMutation> Mutations { get; } = [];

        public bool HoldMutation { get; init; }

        public HttpStatusCode MutationStatusCode { get; init; } = HttpStatusCode.OK;

        public TaskCompletionSource MutationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseMutation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            this.Requests.Add(path);
            if (path == "/api/v1/auth/login")
            {
                return UserManagementApiHandler.Json(new
                {
                    accessToken = "access-token",
                    accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                    refreshToken = "refresh",
                    refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    user = new { id = "admin", email = "contact@example.invalid" }
                });
            }

            if (path == "/api/v1/admin/current-user")
            {
                return UserManagementApiHandler.Json(new AdminProfile
                {
                    Id = "admin",
                    Email = "contact@example.invalid",
                    Permissions = ["Admin.Access", .. this._permissions]
                });
            }

            if (path == "/api/v1/auth/revoke")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path == "/api/v1/admin/billing/available-plans")
            {
                return UserManagementApiHandler.Json(new[]
                {
                    new AdminAvailablePlan { Id = 10, Name = "Личный", PlanTypeCode = "personal", DurationDays = 30 },
                    new AdminAvailablePlan { Id = 11, Name = "Бизнес", PlanTypeCode = "business", DurationDays = 30 }
                });
            }

            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            this.Mutations.Add(new CapturedMutation(path, body));
            if (this.HoldMutation)
            {
                this.MutationStarted.TrySetResult();
                await this.ReleaseMutation.Task.WaitAsync(cancellationToken);
            }

            return this.MutationStatusCode == HttpStatusCode.OK
                ? UserManagementApiHandler.Json(new AdminOperationResponse { Message = "Операция выполнена." })
                : new HttpResponseMessage(this.MutationStatusCode)
                {
                    Content = JsonContent.Create(new { message = "Срок подписки изменился." })
                };
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value, options: UserManagementApiHandler.s_jsonOptions)
        };
    }

    private sealed record CapturedMutation(string Path, string Body);
}
