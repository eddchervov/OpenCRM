using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenCRM.Admin.Api;

namespace OpenCRM.Admin.Api.Tests.Integration;

public sealed class AdminSessionHttpTests
{
    [Fact]
    public async Task LoginUsesAuthEndpointAndLoadsProfileWithBearerToken()
    {
        var requests = new List<(string Method, string Path, string? Authorization, string? Body)>();
        using var httpClient = new HttpClient(new FakeHttpMessageHandler(async (request, cancellationToken) =>
        {
            var body = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter, body));

            return request.RequestUri.AbsolutePath switch
            {
                "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
                "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, new
                {
                    id = "admin-1",
                    email = "contact@example.invalid",
                    roles = new[] { "Admin" },
                    permissions = new[] { "Admin.Access" },
                    isSystem = false
                }),
                _ => throw new InvalidOperationException("Неожиданный запрос.")
            };
        }))
        {
            BaseAddress = new Uri("https://opencrm.test/")
        };
        using var session = new AdminSessionClient(httpClient);

        var profile = await session.LoginAsync("contact@example.invalid", "password");

        Assert.Equal("admin-1", profile.Id);
        Assert.Collection(requests,
            request =>
            {
                Assert.Equal("POST", request.Method);
                Assert.Equal("/api/v1/auth/login", request.Path);
                Assert.Null(request.Authorization);
                using var loginBody = JsonDocument.Parse(request.Body!);
                Assert.Equal("contact@example.invalid", loginBody.RootElement.GetProperty("email").GetString());
                Assert.Equal("password", loginBody.RootElement.GetProperty("password").GetString());
            },
            request =>
            {
                Assert.Equal("GET", request.Method);
                Assert.Equal("/api/v1/admin/current-user", request.Path);
                Assert.Equal("access-token", request.Authorization);
                Assert.Null(request.Body);
            });
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object value)
    {
        return new HttpResponseMessage(statusCode) { Content = JsonContent.Create(value) };
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

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handler(request, cancellationToken);
        }
    }
}
