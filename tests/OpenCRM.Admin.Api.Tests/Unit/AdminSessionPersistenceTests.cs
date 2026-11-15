using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api.Tests.Unit;

public sealed class AdminSessionPersistenceTests
{
    [Fact]
    public async Task LoginAndDisposePreserveRefreshTokenForNextClient()
    {
        var store = new MemorySessionStore();
        using var httpClient = CreateClient();
        using (var session = new AdminSessionClient(httpClient, sessionStore: store))
        {
            await session.LoginAsync("contact@example.invalid", "password");
            Assert.Equal("login-refresh", store.Session?.RefreshToken);
        }

        Assert.NotNull(store.Session);
        using var restarted = new AdminSessionClient(httpClient, sessionStore: store);

        var profile = await restarted.RestoreSessionAsync();

        Assert.Equal("admin-1", profile?.Id);
        Assert.True(restarted.HasSession);
        Assert.Equal("rotated-refresh", store.Session?.RefreshToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RejectedRefreshDeletesSavedSession(HttpStatusCode status)
    {
        var store = CreateSavedStore();
        using var httpClient = CreateClient(refreshStatus: status);
        using var session = new AdminSessionClient(httpClient, sessionStore: store);

        await Assert.ThrowsAsync<ApiException>(() => session.RestoreSessionAsync());

        Assert.Null(store.Session);
        Assert.False(session.HasSession);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task TemporaryRefreshFailurePreservesSavedSession(HttpStatusCode status)
    {
        var store = CreateSavedStore();
        using var httpClient = CreateClient(refreshStatus: status);
        using var session = new AdminSessionClient(httpClient, sessionStore: store);

        await Assert.ThrowsAsync<ApiException>(() => session.RestoreSessionAsync());

        Assert.Equal("saved-refresh", store.Session?.RefreshToken);
        Assert.False(session.HasSession);
    }

    [Fact]
    public async Task NetworkFailurePreservesSavedSession()
    {
        var store = CreateSavedStore();
        using var httpClient = new HttpClient(new DelegateHandler(_ => throw new HttpRequestException()))
        {
            BaseAddress = new Uri("https://opencrm.test/")
        };
        using var session = new AdminSessionClient(httpClient, sessionStore: store);

        await Assert.ThrowsAsync<ApiException>(() => session.RestoreSessionAsync());

        Assert.Equal("saved-refresh", store.Session?.RefreshToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FailedManualLoginPreservesPreviouslySavedSession(HttpStatusCode status)
    {
        var store = CreateSavedStore();
        using var httpClient = CreateClient(loginStatus: status);
        using var session = new AdminSessionClient(httpClient, sessionStore: store);

        await Assert.ThrowsAsync<ApiException>(() => session.LoginAsync("contact@example.invalid", "wrong-password"));

        Assert.Equal("saved-refresh", store.Session?.RefreshToken);
        Assert.False(session.HasSession);
    }

    [Fact]
    public async Task ProfileFailurePreservesRotatedTokenForNextAttempt()
    {
        var store = CreateSavedStore();
        using var unavailableClient = CreateClient(profileStatus: HttpStatusCode.ServiceUnavailable);
        using (var session = new AdminSessionClient(unavailableClient, sessionStore: store))
        {
            await Assert.ThrowsAsync<ApiException>(() => session.RestoreSessionAsync());
        }

        Assert.Equal("rotated-refresh", store.Session?.RefreshToken);
        using var workingClient = CreateClient(expectedRefresh: "rotated-refresh");
        using var restarted = new AdminSessionClient(workingClient, sessionStore: store);

        Assert.NotNull(await restarted.RestoreSessionAsync());
    }

    [Fact]
    public async Task ForbiddenProfileDeletesSavedSessionAndRevokesToken()
    {
        var store = CreateSavedStore();
        var revoked = false;
        using var httpClient = CreateClient(profileStatus: HttpStatusCode.Forbidden, onRevoke: () => revoked = true);
        using var session = new AdminSessionClient(httpClient, sessionStore: store);

        await Assert.ThrowsAsync<ApiException>(() => session.RestoreSessionAsync());

        Assert.Null(store.Session);
        Assert.Null(session.CurrentProfile);
        Assert.True(revoked);
    }

    [Fact]
    public async Task ExpiredSavedSessionIsDeletedWithoutHttpRequest()
    {
        var store = CreateSavedStore();
        store.Session = store.Session! with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        using var httpClient = new HttpClient(new DelegateHandler(_ => throw new InvalidOperationException("Запрос не ожидался.")));
        using var session = new AdminSessionClient(httpClient, sessionStore: store);

        Assert.Null(await session.RestoreSessionAsync());
        Assert.Null(store.Session);
    }

    [Fact]
    public async Task MissingSavedSessionDoesNotMakeHttpRequest()
    {
        using var httpClient = new HttpClient(new DelegateHandler(_ => throw new InvalidOperationException("Запрос не ожидался.")));
        using var session = new AdminSessionClient(httpClient, sessionStore: new MemorySessionStore());

        Assert.Null(await session.RestoreSessionAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task LogoutDeletesSavedSessionEvenWhenRevokeFails(HttpStatusCode revokeStatus)
    {
        var store = CreateSavedStore();
        using var httpClient = CreateClient(revokeStatus: revokeStatus);
        using var session = new AdminSessionClient(httpClient, sessionStore: store);
        await session.RestoreSessionAsync();

        if (revokeStatus == HttpStatusCode.NoContent)
        {
            await session.LogoutAsync();
        }
        else
        {
            await Assert.ThrowsAsync<ApiException>(() => session.LogoutAsync());
        }

        Assert.Null(store.Session);
        Assert.False(session.HasSession);
    }

    [Fact]
    public async Task ProactiveRefreshSavesRotatedToken()
    {
        var store = new MemorySessionStore();
        using var httpClient = CreateClient(loginExpiresAt: DateTimeOffset.UtcNow.AddSeconds(10), expectedRefresh: "login-refresh");
        using var session = new AdminSessionClient(httpClient, sessionStore: store);

        await session.LoginAsync("contact@example.invalid", "password");

        Assert.Equal("rotated-refresh", store.Session?.RefreshToken);
    }

    private static MemorySessionStore CreateSavedStore() => new()
    {
        Session = new SavedAdminSession("admin-1", "saved-refresh", DateTimeOffset.UtcNow.AddDays(7))
    };

    private static HttpClient CreateClient(
        HttpStatusCode refreshStatus = HttpStatusCode.OK,
        HttpStatusCode profileStatus = HttpStatusCode.OK,
        HttpStatusCode revokeStatus = HttpStatusCode.NoContent,
        string expectedRefresh = "saved-refresh",
        Action? onRevoke = null,
        DateTimeOffset? loginExpiresAt = null,
        HttpStatusCode loginStatus = HttpStatusCode.OK)
    {
        return new HttpClient(new DelegateHandler(request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api/v1/auth/login":
                    return loginStatus == HttpStatusCode.OK ? Tokens("login-refresh", loginExpiresAt) : new HttpResponseMessage(loginStatus);
                case "/api/v1/auth/refresh":
                    using (var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()))
                    {
                        var refresh = body.RootElement.GetProperty("refreshToken").GetString();
                        Assert.True(refresh == expectedRefresh || refresh == "login-refresh");
                        Assert.Equal("admin-1", body.RootElement.GetProperty("userId").GetString());
                    }

                    return refreshStatus == HttpStatusCode.OK ? Tokens("rotated-refresh") : new HttpResponseMessage(refreshStatus);
                case "/api/v1/admin/current-user":
                    Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);

                    return new HttpResponseMessage(profileStatus)
                    {
                        Content = JsonContent.Create(new
                        {
                            id = "admin-1",
                            email = "contact@example.invalid",
                            roles = new[] { "Admin" },
                            permissions = new[] { "Admin.Access" }
                        })
                    };
                case "/api/v1/auth/revoke":
                    onRevoke?.Invoke();

                    return new HttpResponseMessage(revokeStatus);
                default:
                    throw new InvalidOperationException("Неожиданный запрос.");
            }
        })) { BaseAddress = new Uri("https://opencrm.test/") };
    }

    private static HttpResponseMessage Tokens(string refreshToken, DateTimeOffset? expiresAt = null) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new
        {
            accessToken = "access-token",
            accessTokenExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(1),
            refreshToken,
            refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            tokenType = "Bearer",
            user = new { id = "admin-1", roles = new[] { "Admin" } }
        })
    };

    private sealed class MemorySessionStore : IAdminSessionStore
    {
        public SavedAdminSession? Session { get; set; }

        public SavedAdminSession? Load() => this.Session;

        public void Save(SavedAdminSession session) => this.Session = session;

        public void Delete() => this.Session = null;
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handle(request));
    }
}
