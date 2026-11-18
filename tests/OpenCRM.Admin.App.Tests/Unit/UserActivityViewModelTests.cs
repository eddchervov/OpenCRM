using System.Net;
using System.Net.Http.Json;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Unit;

[Collection("Admin localization")]
public sealed class UserActivityViewModelTests : IDisposable
{
    private readonly string _preferencePath = Path.Combine(Path.GetTempPath(), $"opencrm-admin-activity-language-{Guid.NewGuid():N}.txt");
    private readonly IDisposable _preferenceScope;
    private readonly string _previousLanguage;

    public UserActivityViewModelTests()
    {
        this._preferenceScope = AdminLocalization.Instance.UsePreferencePath(this._preferencePath);
        this._previousLanguage = AdminLocalization.Instance.Language;
        AdminLocalization.Instance.Language = "ru";
    }

    public void Dispose()
    {
        AdminLocalization.Instance.Language = this._previousLanguage;
        this._preferenceScope.Dispose();
        if (File.Exists(this._preferencePath))
        {
            File.Delete(this._preferencePath);
        }
    }

    [Theory]
    [InlineData("create_booking", "Создание записи")]
    [InlineData("add_service", "Добавление услуги")]
    [InlineData("view_calendar", "Просмотр расписания")]
    [InlineData("checkout_started", "Начато оформление оплаты")]
    [InlineData("open_detailing", "Открытие детейлинга")]
    [InlineData("unknown_action", "unknown_action")]
    public void ActivityRowsShowReadableLabelsWithoutTechnicalDetails(string action, string expectedLabel)
    {
        var row = new UserActivityRow(new AdminUserActivityItem(
            1, "user-1", "contact@example.invalid", null, null, Guid.Empty,
            DateTime.UtcNow, DateTime.UtcNow, "click", action, "/page", "detailing", "scheduler", null));

        Assert.Equal(expectedLabel, row.Event);
        Assert.Equal("contact@example.invalid", row.User);
        Assert.Equal("Без организации", row.Organization);
        Assert.Equal("Детейлинг · Расписание", row.Location);
    }

    [Fact]
    public void ActivityBuiltInsChangeWithLanguageWhileUnknownCodesRemainRaw()
    {
        var known = new UserActivityRow(new AdminUserActivityItem(
            1, "user-1", "contact@example.invalid", null, null, Guid.Empty,
            DateTime.UtcNow, DateTime.UtcNow, "click", "create_booking", "/page", "detailing", "scheduler", null));
        var unknown = new UserActivityRow(new AdminUserActivityItem(
            2, "user-1", "contact@example.invalid", null, null, Guid.Empty,
            DateTime.UtcNow, DateTime.UtcNow, "click", "vendor_action", "/page", "unknown-scenario", "unknown-section", null));

        Assert.Equal("Создание записи", known.Event);
        Assert.Equal("unknown-scenario · unknown-section", unknown.Location);

        AdminLocalization.Instance.Language = "en";
        Assert.Equal("Create booking", known.Event);
        Assert.Equal("unknown-scenario · unknown-section", unknown.Location);

        AdminLocalization.Instance.Language = "zh-Hans";
        Assert.Equal("创建预约", known.Event);
        Assert.Equal("unknown-scenario · unknown-section", unknown.Location);
    }

    [Theory]
    [InlineData("/clients/detailing/orders/{id}", "Детейлинг · Заказы")]
    [InlineData("/clients/detailing/orders/{id}/edit", "Детейлинг · Заказы · Редактирование")]
    [InlineData("/clients/detailing/clients/{id}", "Детейлинг · Клиенты")]
    [InlineData("/detailing/{id}", "Детейлинг")]
    public void PageLabelsDistinguishPagesWithoutExposingRoutesOrIds(string page, string expected)
    {
        var row = new UserActivityRow(new AdminUserActivityItem(
            1, "user-1", "contact@example.invalid", null, null, Guid.Empty,
            DateTime.UtcNow, DateTime.UtcNow, "page", "view", page, "detailing", "detailing", null));

        Assert.Equal(expected, row.Location);
    }

    [Fact]
    public void LanguageRefreshPreservesActivityFiltersAndSelectedCodes()
    {
        var previousLanguage = AdminLocalization.Instance.Language;
        using var httpClient = new HttpClient();
        using var session = new AdminSessionClient(httpClient);
        var model = new UserActivityViewModel(session);
        model.Kind = "page";
        model.Scenario = "fleet";
        model.Search = "keep this search";
        model.FromDate = "2026-01-02";

        try
        {
            AdminLocalization.Instance.Language = "ru";
            model.RefreshLocalizedValues();
            Assert.Equal("page", model.SelectedKindOption.Code);
            Assert.Equal("fleet", model.SelectedScenarioOption.Code);

            AdminLocalization.Instance.Language = "en";
            model.RefreshLocalizedValues();
            Assert.Equal("Page view", model.SelectedKindOption.Title);
            Assert.Equal("Fleet", model.SelectedScenarioOption.Title);
            Assert.Equal("keep this search", model.Search);
            Assert.Equal("2026-01-02", model.FromDate);

            AdminLocalization.Instance.Language = "zh-Hans";
            model.RefreshLocalizedValues();
            Assert.Equal("页面访问", model.SelectedKindOption.Title);
            Assert.Equal("车队", model.SelectedScenarioOption.Title);
            Assert.Equal("page", model.SelectedKindOption.Code);
            Assert.Equal("fleet", model.SelectedScenarioOption.Code);
            Assert.Equal("keep this search", model.Search);
            Assert.Equal("2026-01-02", model.FromDate);
        }
        finally
        {
            AdminLocalization.Instance.Language = previousLanguage;
        }
    }

    [Fact]
    public async Task ClearingActivityErrorOnStopDoesNotRestoreItAfterLanguageChange()
    {
        using var client = CreateClient((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/auth/login")
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, CreateTokens()));
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/current-user")
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, CreateProfile()));
            }

            return Task.FromResult(JsonResponse(HttpStatusCode.OK, CreatePage(100, null)));
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new UserActivityViewModel(session);
        await model.ActivateAsync();
        model.FromDate = "not-a-date";
        await model.ApplyFiltersCommand.ExecuteAsync(null);

        Assert.Equal("Дата начала должна быть в формате ГГГГ-ММ-ДД.", model.ErrorMessage);
        await model.DeactivateAsync(clear: true);
        AdminLocalization.Instance.Language = "en";
        model.RefreshLocalizedValues();

        Assert.Empty(model.ErrorMessage);
    }

    [Fact]
    public async Task SelectedUserIsAppliedToCursorPagesAndCanBeCleared()
    {
        var activityQueries = new List<string>();
        string? userQuery = null;
        using var client = CreateClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());
            if (path == "/api/v1/admin/users")
            {
                userQuery = request.RequestUri.Query;
                return JsonResponse(HttpStatusCode.OK, new AdminUsersResponse
                {
                    Users = [new AdminUserListItem { Id = "user-2", Email = "contact@example.invalid" }]
                });
            }

            activityQueries.Add(request.RequestUri.Query);
            return JsonResponse(HttpStatusCode.OK, CreatePage(100, 50));
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new UserActivityViewModel(session);
        await model.ActivateAsync();

        var users = await model.PopulateUsers("second@", TestContext.Current.CancellationToken);
        model.SelectedUser = Assert.IsType<ActivityUserOption>(Assert.Single(users));
        model.UserSearch = model.SelectedUser.Label;
        await model.NextPageCommand.ExecuteAsync(null);

        Assert.Equal("second@", ParseQuery(userQuery!)["search"]);
        Assert.Equal("true", ParseQuery(userQuery!)["includeDeleted"]);
        Assert.Equal("user-2", ParseQuery(activityQueries[1])["userId"]);
        Assert.Null(ParseQuery(activityQueries[1]).GetValueOrDefault("beforeId"));
        await model.NextPageCommand.ExecuteAsync(null);
        Assert.Equal("user-2", ParseQuery(activityQueries[2])["userId"]);
        Assert.Equal("50", ParseQuery(activityQueries[2])["beforeId"]);

        model.ClearUserCommand.Execute(null);
        await model.ApplyFiltersCommand.ExecuteAsync(null);
        Assert.Null(ParseQuery(activityQueries[3]).GetValueOrDefault("userId"));
        Assert.Null(model.SelectedUser);
        Assert.Empty(model.UserSearch);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task FixedUserCannotBeReplacedOrCleared()
    {
        var queries = new List<string>();
        using var client = CreateClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());
            queries.Add(request.RequestUri.Query);
            return JsonResponse(HttpStatusCode.OK, CreatePage(100, null));
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new UserActivityViewModel(session);
        await model.ActivateAsync("user-1");

        Assert.Equal("contact@example.invalid", model.SelectedUser?.Label);
        model.ClearUserCommand.Execute(null);
        Assert.NotNull(model.SelectedUser);
        Assert.Empty(await model.PopulateUsers("other", TestContext.Current.CancellationToken));
        model.SelectedUser = new ActivityUserOption("other", "contact@example.invalid");
        await model.ApplyFiltersCommand.ExecuteAsync(null);

        Assert.All(queries, query => Assert.Equal("user-1", ParseQuery(query)["userId"]));
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task LeavingActivityCancelsUserSearchAndDiscardsItsResults()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        using var client = CreateClient(async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());
            if (path != "/api/v1/admin/users") return JsonResponse(HttpStatusCode.OK, CreatePage(100, null));
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                throw;
            }

            return JsonResponse(HttpStatusCode.OK, new AdminUsersResponse());
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new UserActivityViewModel(session);
        await model.ActivateAsync();
        var search = model.PopulateUsers("second", TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await model.DeactivateAsync(true);

        Assert.Empty(await search);
        Assert.True(cancelled);
        Assert.Empty(model.Users);
        Assert.False(model.IsSearchingUsers);
        Assert.False(model.HasError);
    }

    [Fact]
    public async Task ChangedFiltersLoadNewFirstPageBeforeAnyFurtherCursorRequest()
    {
        var activityQueries = new List<string>();
        using var client = CreateClient(async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());

            activityQueries.Add(request.RequestUri.Query);
            var cursor = ParseQuery(request.RequestUri.Query).GetValueOrDefault("beforeId");
            var id = cursor == "80" ? 300L : cursor == null ? activityQueries.Count == 1 ? 100L : 200L : 999L;
            var nextBeforeId = id switch { 100 => 50L, 200 => 80L, _ => (long?)null };
            return JsonResponse(HttpStatusCode.OK, CreatePage(id, nextBeforeId));
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new UserActivityViewModel(session);

        await model.ActivateAsync();
        model.Search = "заказ";
        model.Kind = "Переход";
        model.Scenario = "Детейлинг";
        model.FromDate = "2026-10-21";
        model.ToDate = "2026-10-21";
        await model.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(2, activityQueries.Count);
        var changedFilters = ParseQuery(activityQueries[1]);
        Assert.Equal("заказ", changedFilters["search"]);
        Assert.Equal("page", changedFilters["kind"]);
        Assert.Equal("detailing", changedFilters["scenario"]);
        Assert.Equal("2026-10-21T00:00:00.0000000Z", changedFilters["from"]);
        Assert.Equal("2026-10-21T23:59:59.9999999Z", changedFilters["to"]);
        Assert.Null(changedFilters.GetValueOrDefault("beforeId"));
        Assert.Equal(200, Assert.Single(model.Items).Item.Id);

        await model.NextPageCommand.ExecuteAsync(null);

        Assert.Equal("80", ParseQuery(activityQueries[2])["beforeId"]);
        Assert.Equal(300, Assert.Single(model.Items).Item.Id);

        await model.PreviousPageCommand.ExecuteAsync(null);

        Assert.Null(ParseQuery(activityQueries[3]).GetValueOrDefault("beforeId"));
        Assert.Equal(200, Assert.Single(model.Items).Item.Id);
        Assert.False(model.CanGoPrevious);

        await model.NextPageCommand.ExecuteAsync(null);
        model.Search = "клиент";
        await model.PreviousPageCommand.ExecuteAsync(null);

        Assert.Null(ParseQuery(activityQueries[5]).GetValueOrDefault("beforeId"));
        Assert.Equal("клиент", ParseQuery(activityQueries[5])["search"]);
        Assert.Equal(200, Assert.Single(model.Items).Item.Id);
        Assert.False(model.CanGoPrevious);
        await model.DeactivateAsync(clear: true);
    }

    [Fact]
    public async Task DeactivateCancelsPendingRequestAndClearsLoadedEvents()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCancelled = false;
        using var client = CreateClient(async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());

            requestStarted.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                requestCancelled = true;
                throw;
            }

            return JsonResponse(HttpStatusCode.OK, CreatePage(1, null));
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new UserActivityViewModel(session);

        var activation = model.ActivateAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await model.DeactivateAsync(clear: true);
        await activation;

        Assert.True(requestCancelled);
        Assert.Empty(model.Items);
        Assert.False(model.IsLoading);
    }

    [Fact]
    public async Task EachCursorPageDisplaysAtMostOneHundredRows()
    {
        using var client = CreateClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());
            return JsonResponse(HttpStatusCode.OK, CreatePage(1, 101, 125));
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new UserActivityViewModel(session);

        await model.ActivateAsync();

        Assert.Equal(100, model.Items.Count);
        Assert.True(model.HasMore);
        await model.DeactivateAsync(clear: true);
    }

    [Fact]
    public async Task RequestTimeoutIsShownInsteadOfEscapingTheCommand()
    {
        using var client = CreateClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());
            throw new TaskCanceledException("timeout");
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new UserActivityViewModel(session);

        await model.ActivateAsync();

        Assert.Equal("Превышено время ожидания ответа сервера. Повторите попытку.", model.ErrorMessage);
        Assert.False(model.IsLoading);
        await model.DeactivateAsync(clear: true);
    }

    [Fact]
    public async Task MaximumDateCanBeUsedAsInclusiveEndDate()
    {
        string? activityQuery = null;
        using var client = CreateClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());
            activityQuery = request.RequestUri.Query;
            return JsonResponse(HttpStatusCode.OK, CreatePage(1, null));
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new UserActivityViewModel(session) { ToDate = "9999-12-31" };

        await model.ActivateAsync();

        Assert.Equal("9999-12-31T23:59:59.9999999Z", ParseQuery(activityQuery!)["to"]);
        Assert.False(model.HasError);
        await model.DeactivateAsync(clear: true);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task UnauthorizedAndForbiddenResponsesRaiseTheirSessionEvents(HttpStatusCode statusCode)
    {
        using var client = CreateClient((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login" || path == "/api/v1/auth/refresh")
            {
                return JsonResponse(HttpStatusCode.OK, CreateTokens());
            }

            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());
            return JsonResponse(statusCode, new { message = "Недостаточно прав." });
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new UserActivityViewModel(session);
        var sessionExpiredCount = 0;
        var accessDeniedCount = 0;
        model.SessionExpired += () => sessionExpiredCount++;
        model.AccessDenied += () =>
        {
            accessDeniedCount++;
            return Task.CompletedTask;
        };

        await model.ActivateAsync();

        Assert.Equal(statusCode == HttpStatusCode.Unauthorized ? 1 : 0, sessionExpiredCount);
        Assert.Equal(statusCode == HttpStatusCode.Forbidden ? 1 : 0, accessDeniedCount);
        Assert.True(model.HasError);
        await model.DeactivateAsync(clear: true);
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new FakeHttpMessageHandler(handler)) { BaseAddress = new Uri("https://opencrm.test/") };

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) =>
        CreateClient((request, cancellationToken) => Task.FromResult(handler(request, cancellationToken)));

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object value) =>
        new(statusCode) { Content = JsonContent.Create(value) };

    private static object CreateTokens()
    {
        var now = DateTimeOffset.UtcNow;
        return new
        {
            accessToken = "access-token",
            accessTokenExpiresAt = now.AddHours(1),
            refreshToken = "refresh-token",
            refreshTokenExpiresAt = now.AddDays(10),
            tokenType = "Bearer",
            user = new { id = "admin-1", email = "contact@example.invalid", userName ="admin", roles = new[] { "Admin" } }
        };
    }

    private static object CreateProfile() =>
        new { id = "admin-1", email = "contact@example.invalid", roles = new[] { "Admin" }, permissions = new[] { "Admin.Access" } };

    private static object CreatePage(long id, long? nextBeforeId, int itemCount = 1) => new
    {
        enabled = true,
        retentionDays = 90,
        nextBeforeId,
        items = Enumerable.Range(0, itemCount)
            .Select(index => new
        {
            id = id + index,
            userId = "user-1",
            userLabel = "contact@example.invalid",
            organizationId = (Guid?)null,
            organizationName = (string?)null,
            sessionId = Guid.Parse("c69ecf33-b6e3-4dde-a57a-99e967a7c118"),
            occurredAt = DateTime.UtcNow,
            receivedAt = DateTime.UtcNow,
            kind = "click",
            action = "create_order",
            page = "/orders/{id}",
            scenario = "detailing",
            section = "orders",
            targetId = "order-8"
        })
        .ToArray()
    };

    private static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .ToDictionary(part => Uri.UnescapeDataString(part[0]), part => Uri.UnescapeDataString(part[1]));

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
