using System.Net;
using System.Net.Http.Json;
using OpenCRM.Admin.Api;

namespace OpenCRM.Admin.Api.Tests.Integration;

public sealed class AdminSessionClientUserActivityTests
{
    [Fact]
    public async Task ActivityRequestUsesFiltersCursorAndParsesPage()
    {
        string? requestQuery = null;
        using var client = new HttpClient(new FakeHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());

            requestQuery = request.RequestUri.Query;
            return JsonResponse(HttpStatusCode.OK, new
            {
                enabled = true,
                retentionDays = 90,
                nextBeforeId = 12L,
                items = new[]
                {
                    new
                    {
                        id = 15L,
                        userId = "user-1",
                        userLabel = "contact@example.invalid",
                        organizationId = (Guid?)null,
                        organizationName = (string?)null,
                        sessionId = Guid.Parse("c69ecf33-b6e3-4dde-a57a-99e967a7c118"),
                        occurredAt = DateTime.UtcNow,
                        receivedAt = DateTime.UtcNow,
                        kind = "operation",
                        action = "POST:success:200",
                        page = "/orders/{id}",
                        scenario = "detailing",
                        section = "orders",
                        targetId = "order-8"
                    }
                }
            });
        })) { BaseAddress = new Uri("https://opencrm.test/") };
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 10, 21, 23, 59, 59, TimeSpan.Zero);

        var page = await session.GetUserActivityAsync("user/1", "order #8", "operation", "detailing",
            from: from, to: to, beforeId: 20, pageSize: 100);

        Assert.Contains("userId=user%2F1", requestQuery);
        Assert.Contains("search=order%20%238", requestQuery);
        Assert.Contains("kind=operation", requestQuery);
        Assert.Contains("scenario=detailing", requestQuery);
        Assert.Contains("from=2026-10-01T00%3A00%3A00.0000000Z", requestQuery);
        Assert.Contains("to=2026-10-21T23%3A59%3A59.0000000Z", requestQuery);
        Assert.Contains("beforeId=20", requestQuery);
        Assert.Contains("pageSize=100", requestQuery);
        Assert.True(page.Enabled);
        Assert.Equal(90, page.RetentionDays);
        Assert.Equal(12, page.NextBeforeId);
        var activity = Assert.Single(page.Items);
        Assert.Equal("POST:success:200", activity.Action);
        Assert.Equal("/orders/{id}", activity.Page);
        Assert.Equal("order-8", activity.TargetId);
    }

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

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request, cancellationToken));
    }
}
