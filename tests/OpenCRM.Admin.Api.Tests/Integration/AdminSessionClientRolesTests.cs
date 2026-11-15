using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenCRM.Admin.Api;

namespace OpenCRM.Admin.Api.Tests.Integration;

public sealed class AdminSessionClientRolesTests
{
    [Fact]
    public async Task UpdateRolePermissionsSendsPutWithPermissionCodes()
    {
        JsonElement? requestBody = null;
        using var client = CreateClient(async (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/auth/login")
            {
                return JsonResponse(HttpStatusCode.OK, CreateTokens());
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/current-user")
            {
                return JsonResponse(HttpStatusCode.OK, CreateProfile());
            }

            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/api/v1/admin/permissions/roles/manager%2Frole", request.RequestUri.AbsolutePath);
            requestBody = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone();
            return JsonResponse(HttpStatusCode.OK, new { message = "Права сохранены." });
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var result = await session.UpdateAdminRolePermissionsAsync("manager/role", ["Admin.Access", "Admin.Users.View"]);

        Assert.Equal("Права сохранены.", result.Message);
        Assert.Equal(new[] { "Admin.Access", "Admin.Users.View" }, requestBody!.Value.GetProperty("permissionCodes").EnumerateArray()
            .Select(permission => permission.GetString()).ToArray());
    }

    [Fact]
    public async Task RemoveRoleMemberSendsDeleteWithoutBody()
    {
        using var client = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile()),
            "/api/v1/admin/roles/role-1/members/user-1" => DeleteResponse(request),
            _ => throw new InvalidOperationException("Неожиданный запрос.")
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        var result = await session.RemoveAdminRoleMemberAsync("role-1", "user-1");

        Assert.Equal("Роль снята.", result.Message);
    }

    [Fact]
    public async Task UnauthorizedPermissionUpdateRefreshesAndRetriesPutWithBody()
    {
        var updateRequests = new List<(string? Token, string? Body)>();
        using var client = CreateClient(async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
            {
                return JsonResponse(HttpStatusCode.OK, CreateTokens());
            }

            if (path == "/api/v1/admin/current-user")
            {
                return JsonResponse(HttpStatusCode.OK, CreateProfile());
            }

            if (path == "/api/v1/auth/refresh")
            {
                return JsonResponse(HttpStatusCode.OK, CreateTokens("rotated-access", "rotated-refresh"));
            }

            Assert.Equal("/api/v1/admin/permissions/roles/role-1", path);
            updateRequests.Add((request.Headers.Authorization?.Parameter, await request.Content!.ReadAsStringAsync()));
            return updateRequests.Count == 1
                ? JsonResponse(HttpStatusCode.Unauthorized, new { message = "Токен истек." })
                : JsonResponse(HttpStatusCode.OK, new { message = "Права сохранены." });
        });
        using var session = new AdminSessionClient(client);
        await session.LoginAsync("contact@example.invalid", "password");

        await session.UpdateAdminRolePermissionsAsync("role-1", ["Admin.Access", "Admin.Billing.View"]);

        Assert.Equal(new[] { "access-token", "rotated-access" }, updateRequests.Select(request => request.Token));
        Assert.Equal(updateRequests[0].Body, updateRequests[1].Body);
        Assert.Contains("Admin.Billing.View", updateRequests[1].Body);
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

    private static HttpResponseMessage DeleteResponse(HttpRequestMessage request)
    {
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Null(request.Content);
        return JsonResponse(HttpStatusCode.OK, new { message = "Роль снята." });
    }

    private static object CreateTokens(string accessToken = "access-token", string refreshToken = "refresh-token")
    {
        var now = DateTimeOffset.UtcNow;
        return new
        {
            accessToken,
            accessTokenExpiresAt = now.AddHours(1),
            refreshToken,
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
