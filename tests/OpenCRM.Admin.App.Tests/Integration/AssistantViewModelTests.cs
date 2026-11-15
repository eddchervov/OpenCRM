using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class AssistantViewModelTests
{
    [Fact]
    public async Task SettingsConflictDoesNotRetryAndAccessAndUnknownCostRequireExplicitValues()
    {
        using var handler = new AssistantHandler(["Admin.Configuration.Manage", "Admin.Billing.View"]);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://opencrm.test/") };
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new AssistantViewModel(session);
        await model.ActivateAsync();

        Assert.Empty(handler.Mutations);
        Assert.True(model.SaveSettingsCommand.CanExecute(null));
        model.IsEnabled = false;
        await model.SaveSettingsCommand.ExecuteAsync(null);

        var settingsSave = Assert.Single(handler.Mutations, mutation => mutation.Path == "/api/v1/admin/assistant/settings");
        Assert.Equal(1, settingsSave.Count);
        Assert.Equal(8, model.Overview!.Settings.Revision);
        Assert.True(model.IsEnabled);
        Assert.Contains("изменились", model.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        model.SelectedOrganization = Assert.Single(model.Organizations);
        model.OrganizationIsEnabled = true;
        model.OrganizationEnabledUntilLocal = new DateTime(2026, 11, 15, 12, 30, 0, DateTimeKind.Local);
        await model.SaveOrganizationAccessCommand.ExecuteAsync(null);
        model.OrganizationEnabledUntilLocal = null;
        await model.SaveOrganizationAccessCommand.ExecuteAsync(null);

        var grants = handler.Mutations.Where(mutation => mutation.Path.EndsWith("/access", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, grants.Length);
        using (var withExpiry = JsonDocument.Parse(grants[0].Body!))
        {
            Assert.NotEqual(JsonValueKind.Null, withExpiry.RootElement.GetProperty("enabledUntil").ValueKind);
        }

        using (var withoutExpiry = JsonDocument.Parse(grants[1].Body!))
        {
            Assert.Equal(JsonValueKind.Null, withoutExpiry.RootElement.GetProperty("enabledUntil").ValueKind);
        }

        var unknown = Assert.Single(model.UsageItems);
        Assert.Equal("unknown", unknown.Status);
        model.SelectedUsage = unknown;
        model.SettlementCostRub = 2.75m;
        Assert.False(model.SettleUsageCommand.CanExecute(null));
        model.SettlementReason = "Сверено по отчёту провайдера.";
        Assert.True(model.SettleUsageCommand.CanExecute(null));
        await model.SettleUsageCommand.ExecuteAsync(null);

        var settlement = Assert.Single(handler.Mutations, mutation => mutation.Path.EndsWith("/settle", StringComparison.Ordinal));
        using var settlementBody = JsonDocument.Parse(settlement.Body!);
        Assert.Equal(2.75m, settlementBody.RootElement.GetProperty("costRub").GetDecimal());
        Assert.Equal("Сверено по отчёту провайдера.", settlementBody.RootElement.GetProperty("reason").GetString());
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task BillingOnlyProfileCanReadUsageButCannotLoadOrChangeAssistantSettings()
    {
        using var handler = new AssistantHandler(["Admin.Billing.View"]);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://opencrm.test/") };
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new AssistantViewModel(session);
        await model.ActivateAsync();

        Assert.False(model.CanManage);
        Assert.True(model.CanViewBilling);
        Assert.True(model.HasUsage);
        Assert.False(model.SaveSettingsCommand.CanExecute(null));
        Assert.DoesNotContain(handler.Requests, path => path == "/api/v1/admin/assistant");
        Assert.Contains(handler.Requests, path => path.StartsWith("/api/v1/admin/assistant/usage", StringComparison.Ordinal));
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task UsagePaginationKeepsAppliedFiltersUntilTheNextExplicitFilterAction()
    {
        using var handler = new AssistantHandler(["Admin.Billing.View"]);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://opencrm.test/") };
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new AssistantViewModel(session);
        await model.ActivateAsync();

        var initialQuery = Assert.Single(handler.UsageQueries);
        model.SelectedChannel = new CommunicationOption("telegram", "Telegram");
        model.SelectedStatus = new CommunicationOption("unknown", "Неизвестная стоимость");
        model.ModelFilter = "text-v2";
        model.UsageFrom = new DateTime(2026, 10, 2);
        model.UsageTo = new DateTime(2026, 10, 3);
        await model.NextUsagePageCommand.ExecuteAsync(null);

        var nextPageQuery = handler.UsageQueries[^1];
        Assert.Contains("page=2", nextPageQuery);
        Assert.DoesNotContain("channel=telegram", nextPageQuery);
        Assert.DoesNotContain("model=text-v2", nextPageQuery);
        Assert.Contains($"from={DateOnly.FromDateTime(DateTime.Today.AddDays(-6)):yyyy-MM-dd}", nextPageQuery);
        Assert.Contains($"to={DateOnly.FromDateTime(DateTime.Today):yyyy-MM-dd}", nextPageQuery);

        await model.FilterUsageCommand.ExecuteAsync(null);
        var filteredQuery = handler.UsageQueries[^1];
        Assert.Contains("page=1", filteredQuery);
        Assert.Contains("channel=telegram", filteredQuery);
        Assert.Contains("status=unknown", filteredQuery);
        Assert.Contains("model=text-v2", filteredQuery);
        Assert.Contains("from=2026-10-02", filteredQuery);
        Assert.Contains("to=2026-10-03", filteredQuery);

        model.ModelFilter = "draft-model";
        await model.NextUsagePageCommand.ExecuteAsync(null);
        var pageWithUnappliedEdit = handler.UsageQueries[^1];
        Assert.Contains("page=2", pageWithUnappliedEdit);
        Assert.Contains("model=text-v2", pageWithUnappliedEdit);
        Assert.DoesNotContain("draft-model", pageWithUnappliedEdit);
        Assert.Equal(4, handler.UsageQueries.Count);
        Assert.NotEmpty(initialQuery);
        await model.DeactivateAsync(true);
    }

    private sealed class AssistantHandler(IReadOnlyList<string> permissions) : HttpMessageHandler
    {
        private int _settingsRevision = 7;

        public List<string> Requests { get; } = [];
        public List<Mutation> Mutations { get; } = [];
        public List<string> UsageQueries { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return Json(HttpStatusCode.OK, Tokens());
            if (path == "/api/v1/admin/current-user") return Json(HttpStatusCode.OK, Profile(permissions));

            Requests.Add(path);
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (request.Method != HttpMethod.Get)
            {
                Mutations.Add(new Mutation(path, body, Mutations.Count + 1));
            }

            if (path == "/api/v1/admin/assistant/settings")
            {
                if (_settingsRevision == 7)
                {
                    _settingsRevision = 8;
                    return Json(HttpStatusCode.Conflict, new { message = "Настройки уже изменились. Обновите страницу." });
                }

                return Json(HttpStatusCode.OK, Settings());
            }

            if (path.EndsWith("/access", StringComparison.Ordinal) || path.EndsWith("/settle", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path == "/api/v1/admin/assistant/usage")
            {
                UsageQueries.Add(request.RequestUri.Query);
                return Json(HttpStatusCode.OK, new
                {
                    totals = new { costRub = (decimal?)null, reservedRub = 3m, inputTokens = 0L, outputTokens = 0L, unknownCount = 1, requestCount = 1 },
                    daily = Array.Empty<object>(), byOrganization = Array.Empty<object>(),
                    items = new[] { new { id = Guid.Parse("b4bfac71-270b-4a7c-a9f4-77a047b2b264"), organizationId = Guid.NewGuid(),
                        organizationName = "Студия", userId = "user", userEmail = "contact@example.invalid", channel = "web", kind = "text",
                        model = "text-v1", status = "unknown", reservedCostRub = 3m, createdAt = DateTimeOffset.UtcNow } },
                    totalCount = 100, page = request.RequestUri.Query.Contains("page=2", StringComparison.Ordinal) ? 2 : 1, pageSize = 50
                });
            }

            return Json(HttpStatusCode.OK, Overview());
        }

        private object Overview() => new
        {
            settings = Settings(),
            limits = new { maximumOutputTokensAllowed = 5000, maximumBudgetRub = 900m,
                maximumReservationRub = 100m, budgetTimeZone = "Asia/Novosibirsk" },
            provider = new { routerConfigured = true, telegramConfigured = false },
            organizations = new[] { new { organizationId = Guid.Parse("c69ecf33-b6e3-4dde-a57a-99e967a7c118"),
                name = "Студия", isEnabled = false, webEnabled = false, telegramEnabled = false, dailyBudgetRub = 0m, monthlyBudgetRub = 0m } },
            audits = Array.Empty<object>(), totalCount = 1, page = 1, pageSize = 20
        };

        private object Settings() => new
        {
            id = 1, isEnabled = _settingsRevision > 7, webEnabled = true, telegramEnabled = false, speechRepliesEnabled = false,
            textModel = "text-v1", speechModel = "speech-v1", voiceModel = "voice-v1", voiceName = "speaker",
            dailyBudgetRub = 100m, monthlyBudgetRub = 500m, textReservationRub = 10m, audioReservationRub = 20m,
            maximumOutputTokens = 3000, revision = _settingsRevision, updatedAt = DateTimeOffset.UtcNow
        };

        private static object Tokens() => new
        {
            accessToken = "access-token", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            refreshToken = "refresh-token", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1), tokenType = "Bearer",
            user = new { id = "admin", email = "contact@example.invalid", userName ="admin", roles = new[] { "Admin" } }
        };

        private static object Profile(IReadOnlyList<string> permissions) => new
        {
            id = "admin", email = "contact@example.invalid", roles = new[] { "Admin" },
            permissions = new[] { "Admin.Access" }.Concat(permissions).ToArray()
        };

        private static HttpResponseMessage Json(HttpStatusCode statusCode, object value) =>
            new(statusCode) { Content = JsonContent.Create(value) };
    }

    private sealed record Mutation(string Path, string? Body, int Count);
}
