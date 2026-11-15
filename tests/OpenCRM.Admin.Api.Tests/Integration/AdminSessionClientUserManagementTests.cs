using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenCRM.Admin.Api;

namespace OpenCRM.Admin.Api.Tests.Integration;

public sealed class AdminSessionClientUserManagementTests
{
    [Fact]
    public async Task UserAndSubscriptionOperationsUseExpectedRoutesAndRequestBodies()
    {
        var requests = new List<(string Method, string Path, string? Authorization, string? Body)>();
        using var client = CreateClient(async (request, _) =>
        {
            var path = request.RequestUri!.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
            if (path == "api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());

            requests.Add((request.Method.Method, path, request.Headers.Authorization?.Parameter,
                request.Content == null ? null : await request.Content.ReadAsStringAsync()));
            return path == "api/v1/admin/billing/available-plans"
                ? JsonResponse(HttpStatusCode.OK, new[]
                {
                    new { id = 7, name = "Профи", planTypeCode = "pro", durationDays = 30 }
                })
                : JsonResponse(HttpStatusCode.OK, new { message = "Операция выполнена." });
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");
        var expectedEndDate = new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

        await session.UpdateTestSubscriptionsAsync("user/id +?", true);
        await session.GrantUserDemoAsync("user/id +?", "Проверка пользователя.");
        await session.RevokeUserDemoAsync("user/id +?", "Завершение проверки.");
        await session.GrantOrganizationDemoAsync("user/id +?", "c69ecf33-b6e3-4dde-a57a-99e967a7c118", "Проверка организации.");
        await session.RevokeOrganizationDemoAsync("user/id +?", "c69ecf33-b6e3-4dde-a57a-99e967a7c118", "Завершение проверки организации.");
        var plans = await session.GetAvailablePlansAsync();
        await session.GrantPlanAsync("users", "user/id +?", 7, 30, "Назначение пользователю.");
        await session.GrantPlanAsync("organizations", "c69ecf33-b6e3-4dde-a57a-99e967a7c118", 7, 60, "Назначение организации.");
        await session.ExtendSubscriptionAsync("users", 12, 14, "Продление пользователя.", expectedEndDate);
        await session.StopSubscriptionAsync("organizations", 13, "Остановка организации.", expectedEndDate);

        Assert.Single(plans);
        Assert.Equal(7, plans[0].Id);
        Assert.Equal("Профи", plans[0].Name);
        Assert.Equal("pro", plans[0].PlanTypeCode);
        Assert.Equal(30, plans[0].DurationDays);
        Assert.Equal(new[]
            {
                ("PUT", "api/v1/admin/users/user%2Fid%20%2B%3F/test-subscriptions"),
                ("POST", "api/v1/admin/billing/users/user%2Fid%20%2B%3F/demo"),
                ("DELETE", "api/v1/admin/billing/users/user%2Fid%20%2B%3F/demo"),
                ("POST", "api/v1/admin/billing/users/user%2Fid%20%2B%3F/organizations/c69ecf33-b6e3-4dde-a57a-99e967a7c118/demo"),
                ("DELETE", "api/v1/admin/billing/users/user%2Fid%20%2B%3F/organizations/c69ecf33-b6e3-4dde-a57a-99e967a7c118/demo"),
                ("GET", "api/v1/admin/billing/available-plans"),
                ("POST", "api/v1/admin/billing/users/user%2Fid%20%2B%3F/plan"),
                ("POST", "api/v1/admin/billing/organizations/c69ecf33-b6e3-4dde-a57a-99e967a7c118/plan"),
                ("POST", "api/v1/admin/billing/subscriptions/users/12/extend"),
                ("POST", "api/v1/admin/billing/subscriptions/organizations/13/stop")
            },
            requests.Select(request => (request.Method, request.Path)));
        Assert.All(requests, request => Assert.Equal("access-token", request.Authorization));
        AssertBodyProperty(requests[0].Body, "canUseTestSubscriptions", true);
        AssertBodyProperty(requests[1].Body, "reason", "Проверка пользователя.");
        AssertBodyProperty(requests[3].Body, "reason", "Проверка организации.");
        AssertBodyProperty(requests[6].Body, "planId", 7);
        AssertBodyProperty(requests[6].Body, "days", 30);
        AssertBodyProperty(requests[6].Body, "reason", "Назначение пользователю.");
        AssertBodyProperty(requests[7].Body, "days", 60);
        AssertBodyProperty(requests[8].Body, "days", 14);
        AssertBodyProperty(requests[8].Body, "expectedEndDate", expectedEndDate);
        AssertBodyProperty(requests[9].Body, "reason", "Остановка организации.");
        AssertBodyProperty(requests[9].Body, "expectedEndDate", expectedEndDate);
    }

    [Fact]
    public async Task GrantPlanRejectsUnsupportedScopeBeforeSendingRequest()
    {
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile()),
            _ => throw new InvalidOperationException("Запрос не должен отправляться.")
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            session.GrantPlanAsync("users/../organizations", "target", 1, 30, "Причина."));
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        return new HttpClient(new FakeHttpMessageHandler(handler)) { BaseAddress = new Uri("https://opencrm.test/") };
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
    {
        return CreateClient((request, cancellationToken) => Task.FromResult(handler(request, cancellationToken)));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object value)
    {
        return new HttpResponseMessage(statusCode) { Content = JsonContent.Create(value) };
    }

    private static void AssertBodyProperty(string? body, string propertyName, string expectedValue)
    {
        using var document = JsonDocument.Parse(body!);
        Assert.Equal(expectedValue, document.RootElement.GetProperty(propertyName).GetString());
    }

    private static void AssertBodyProperty(string? body, string propertyName, bool expectedValue)
    {
        using var document = JsonDocument.Parse(body!);
        Assert.Equal(expectedValue, document.RootElement.GetProperty(propertyName).GetBoolean());
    }

    private static void AssertBodyProperty(string? body, string propertyName, int expectedValue)
    {
        using var document = JsonDocument.Parse(body!);
        Assert.Equal(expectedValue, document.RootElement.GetProperty(propertyName).GetInt32());
    }

    private static void AssertBodyProperty(string? body, string propertyName, DateTime expectedValue)
    {
        using var document = JsonDocument.Parse(body!);
        Assert.Equal(expectedValue, document.RootElement.GetProperty(propertyName).GetDateTime());
    }

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

    private static object CreateProfile()
    {
        return new { id = "admin-1", email = "contact@example.invalid", roles = new[] { "Admin" }, permissions = new[] { "Admin.Access" } };
    }

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handler(request, cancellationToken);
        }
    }
}
