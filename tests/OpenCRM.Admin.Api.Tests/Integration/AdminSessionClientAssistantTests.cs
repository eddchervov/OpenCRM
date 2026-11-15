using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api.Tests.Integration;

public sealed class AdminSessionClientAssistantTests
{
    [Fact]
    public async Task AssistantApiReadsServerLimitsAndSendsExplicitChangesAndUsageFilters()
    {
        var requests = new List<(string Method, string Path, string Query, string? Body)>();
        using var http = new HttpClient(new FakeHttpMessageHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return Json(HttpStatusCode.OK, Tokens());
            if (path == "/api/v1/admin/current-user") return Json(HttpStatusCode.OK, Profile());

            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            requests.Add((request.Method.Method, path, request.RequestUri.Query, body));
            return path.EndsWith("/settings", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, new { id = 1, isEnabled = false, revision = 8 })
                : path.EndsWith("/access", StringComparison.Ordinal)
                    ? Json(HttpStatusCode.NoContent, null)
                    : path.EndsWith("/settle", StringComparison.Ordinal)
                        ? Json(HttpStatusCode.NoContent, null)
                        : Json(HttpStatusCode.OK, new
                        {
                            settings = new { id = 1, isEnabled = true, revision = 7 },
                            limits = new { maximumOutputTokensAllowed = 5000, maximumBudgetRub = 900m,
                                maximumReservationRub = 100m, budgetTimeZone = "Asia/Novosibirsk" },
                            provider = new { routerConfigured = true, telegramConfigured = false },
                            organizations = Array.Empty<object>(),
                            audits = Array.Empty<object>(),
                            totalCount = 0, page = 1, pageSize = 20
                        });
        })) { BaseAddress = new Uri("https://opencrm.test/") };
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password");

        var overview = await session.GetAssistantAsync();
        await session.SaveAssistantSettingsAsync(new AdminAssistantSettingsRequest
        {
            IsEnabled = false, WebEnabled = true, TextModel = "text", SpeechModel = "speech", VoiceModel = "voice",
            VoiceName = "speaker", DailyBudgetRub = 20, MonthlyBudgetRub = 100, TextReservationRub = 5,
            AudioReservationRub = 10, MaximumOutputTokens = 2000, Revision = overview.Settings.Revision
        });
        await session.SaveAssistantOrganizationAccessAsync(Guid.Parse("c69ecf33-b6e3-4dde-a57a-99e967a7c118"),
            new AdminAssistantOrganizationAccessRequest { IsEnabled = true, WebEnabled = true, DailyBudgetRub = 15 });
        await session.GetAssistantUsageAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10),
            channel: "telegram", model: "text-v2", status: "unknown", page: 2);
        await session.SettleAssistantUsageAsync(Guid.Parse("b4bfac71-270b-4a7c-a9f4-77a047b2b264"), 3.5m, "Проверено по кабинету провайдера.");

        Assert.Equal(5000, overview.Limits.MaximumOutputTokensAllowed);
        Assert.Equal(900m, overview.Limits.MaximumBudgetRub);
        Assert.Equal(100m, overview.Limits.MaximumReservationRub);
        Assert.Equal("Asia/Novosibirsk", overview.Limits.BudgetTimeZone);
        Assert.Equal("PUT", requests[1].Method);
        Assert.Equal("/api/v1/admin/assistant/settings", requests[1].Path);
        using (var settings = JsonDocument.Parse(requests[1].Body!))
        {
            Assert.False(settings.RootElement.GetProperty("isEnabled").GetBoolean());
            Assert.Equal(7, settings.RootElement.GetProperty("revision").GetInt64());
        }

        Assert.Contains("channel=telegram", requests[3].Query);
        Assert.Contains("model=text-v2", requests[3].Query);
        Assert.Contains("status=unknown", requests[3].Query);
        Assert.Contains("page=2", requests[3].Query);
        Assert.Equal("POST", requests[4].Method);
        using var settlement = JsonDocument.Parse(requests[4].Body!);
        Assert.Equal(3.5m, settlement.RootElement.GetProperty("costRub").GetDecimal());
        Assert.Equal("Проверено по кабинету провайдера.", settlement.RootElement.GetProperty("reason").GetString());
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, object? value) =>
        new(statusCode) { Content = value == null ? null : JsonContent.Create(value) };

    private static object Tokens() => new
    {
        accessToken = "access-token", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        refreshToken = "refresh-token", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        tokenType = "Bearer", user = new { id = "admin", email = "contact@example.invalid", userName ="admin", roles = new[] { "Admin" } }
    };

    private static object Profile() => new
    {
        id = "admin", email = "contact@example.invalid", roles = new[] { "Admin" },
        permissions = new[] { "Admin.Access", "Admin.Configuration.Manage", "Admin.Billing.View" }
    };

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
