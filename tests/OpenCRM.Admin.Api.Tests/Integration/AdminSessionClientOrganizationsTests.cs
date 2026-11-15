using System.Net;
using System.Net.Http.Json;
using OpenCRM.Admin.Api;

namespace OpenCRM.Admin.Api.Tests.Integration;

public sealed class AdminSessionClientOrganizationsTests
{
    [Fact]
    public async Task OrganizationListAndCardUseAdminRoutesAndDeserializeDetails()
    {
        var requests = new List<(string Method, string Path, string Query)>();
        var organizationId = Guid.Parse("c69ecf33-b6e3-4dde-a57a-99e967a7c118");
        using var http = CreateClient(async (request, _) =>
        {
            var path = request.RequestUri!.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
            if (path == "api/v1/auth/login") return Json(HttpStatusCode.OK, CreateTokens());
            if (path == "api/v1/admin/current-user") return Json(HttpStatusCode.OK, CreateProfile());

            requests.Add((request.Method.Method, path, request.RequestUri.Query));
            return path == "api/v1/admin/organizations"
                ? Json(HttpStatusCode.OK, new
                {
                    allCount = 1,
                    totalCount = 1,
                    page = 1,
                    pageSize = 50,
                    activeCount = 1,
                    organizations = new[]
                    {
                        new { id = organizationId, name = "Студия", owner = "Иван", activeSubscriptionName = "Профи", membersCount = 3 }
                    }
                })
                : Json(HttpStatusCode.OK, new
                {
                    id = organizationId,
                    name = "Студия",
                    scenarioName = "Детейлинг",
                    owner = "Иван",
                    workOrdersCount = 8,
                    completedWorkOrdersCount = 5,
                    workOrdersTotal = 12345.67m,
                    paymentsTotal = 10000m,
                    members = new[] { new { userId = "member-1", user = "Анна", email = "contact@example.invalid", roleCode = "admin" } },
                    pendingInvitations = new[] { new { email = "contact@example.invalid", roleCode = "worker", invitedBy = "Иван" } },
                    recentAudit = new[] { new { eventType = "created", title = "Создана организация" } }
                });
        });
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password");

        var page = await session.GetOrganizationsAsync("Студия и сервис", "user/id+?", "members", false, 2, 25);
        var organization = await session.GetOrganizationAsync(organizationId);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(2, requests.Count);
        Assert.Equal("GET", requests[0].Method);
        Assert.Contains("search=%D0%A1%D1%82%D1%83%D0%B4%D0%B8%D1%8F%20%D0%B8%20%D1%81%D0%B5%D1%80%D0%B2%D0%B8%D1%81", requests[0].Query);
        Assert.Contains("user=user%2Fid%2B%3F", requests[0].Query);
        Assert.Contains("sort=members&descending=false&page=2&pageSize=25", requests[0].Query);
        Assert.Equal($"api/v1/admin/organizations/{organizationId:D}", requests[1].Path);
        Assert.Equal(8, organization.WorkOrdersCount);
        Assert.Equal(5, organization.CompletedWorkOrdersCount);
        Assert.Equal(12345.67m, organization.WorkOrdersTotal);
        Assert.Equal(10000m, organization.PaymentsTotal);
        Assert.Equal("Анна", Assert.Single(organization.Members).User);
        Assert.Equal("contact@example.invalid", Assert.Single(organization.PendingInvitations).Email);
        Assert.Equal("Создана организация", Assert.Single(organization.RecentAudit).Title);
    }

    [Fact]
    public async Task GetOrganizationPropagatesNotFoundResponse()
    {
        using var http = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => Json(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => Json(HttpStatusCode.OK, CreateProfile()),
            _ => Json(HttpStatusCode.NotFound, new { message = "Организация не найдена." })
        });
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password");

        var exception = await Assert.ThrowsAsync<ApiException>(() => session.GetOrganizationAsync(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new FakeHttpMessageHandler(handler)) { BaseAddress = new Uri("https://opencrm.test/") };

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) =>
        CreateClient((request, cancellationToken) => Task.FromResult(handler(request, cancellationToken)));

    private static HttpResponseMessage Json(HttpStatusCode statusCode, object value) =>
        new(statusCode) { Content = JsonContent.Create(value) };

    private static object CreateTokens() => new
    {
        accessToken = "access-token",
        accessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        refreshToken = "refresh-token",
        refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        tokenType = "Bearer",
        user = new { id = "admin", email = "contact@example.invalid", userName ="admin", roles = new[] { "Admin" } }
    };

    private static object CreateProfile() => new
    {
        id = "admin",
        email = "contact@example.invalid",
        roles = new[] { "Admin" },
        permissions = new[] { "Admin.Access", "Admin.Users.View" }
    };

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
