using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api.Tests.Integration;

public sealed class AdminSessionClientPromoCodesTests
{
    [Fact]
    public async Task PromoCodeMethodsSendTheirContractsAndAcceptEmptySuccessResponses()
    {
        JsonElement? saveRequest = null;
        using var http = CreateClient(async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path is "/api/v1/auth/login" or "/api/v1/auth/refresh")
            {
                return JsonResponse(HttpStatusCode.OK, CreateTokens());
            }

            if (path == "/api/v1/admin/current-user")
            {
                return JsonResponse(HttpStatusCode.OK, CreateProfile());
            }

            if (path == "/api/v1/admin/promo-codes")
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                saveRequest = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone();
                return JsonResponse(HttpStatusCode.Created, CreatePromoCode());
            }

            if (path == "/api/v1/admin/promo-codes/7/status")
            {
                Assert.Equal(HttpMethod.Patch, request.Method);
                using var statusBody = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.True(statusBody.RootElement.GetProperty("isActive").GetBoolean());
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            if (path == "/api/v1/admin/promo-codes/7")
            {
                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.Null(request.Content);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            throw new InvalidOperationException($"Неожиданный запрос: {request.Method} {path}");
        });
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password", CancellationToken.None);

        var created = await session.CreatePromoCodeAsync(new AdminPromoCodeSaveRequest(
            "GIFT15", "Подарок", "Описание", true, null, null, null, null, "percentage", 15m, 30, null,
            false, [11, 12], null, 20, false, ["detailing"], null), CancellationToken.None);
        var changed = await session.SetPromoCodeStatusAsync(7, true, CancellationToken.None);
        var deleted = await session.DeletePromoCodeAsync(7, CancellationToken.None);

        Assert.Equal("GIFT15", created.Code);
        Assert.Equal("GIFT15", saveRequest!.Value.GetProperty("code").GetString());
        Assert.Equal(new[] { 11, 12 }, saveRequest.Value.GetProperty("subscriptionPlanIds").EnumerateArray()
            .Select(item => item.GetInt32()).ToArray());
        Assert.NotNull(changed);
        Assert.NotNull(deleted);
    }

    [Fact]
    public async Task EmptyDeleteResponseRefreshesUnauthorizedRequestAndRetries()
    {
        var deleteTokens = new List<string?>();
        using var http = CreateClient(async (request, _) =>
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

            Assert.Equal("/api/v1/admin/promo-codes/19", path);
            deleteTokens.Add(request.Headers.Authorization?.Parameter);
            return deleteTokens.Count == 1
                ? JsonResponse(HttpStatusCode.Unauthorized, new { message = "Токен истёк." })
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password", CancellationToken.None);

        var result = await session.DeletePromoCodeAsync(19, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(new[] { "access-token", "rotated-access" }, deleteTokens);
    }

    [Fact]
    public async Task EmptyDeleteResponseFromPreviousSessionIsRejected()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loginCount = 0;
        using var http = CreateClient(async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
            {
                loginCount++;
                return JsonResponse(HttpStatusCode.OK, CreateTokens($"access-{loginCount}", $"refresh-{loginCount}"));
            }

            if (path == "/api/v1/admin/current-user")
            {
                return JsonResponse(HttpStatusCode.OK, CreateProfile());
            }

            if (path == "/api/v1/auth/revoke")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            Assert.Equal("/api/v1/admin/promo-codes/23", path);
            requestStarted.TrySetResult();
            await releaseRequest.Task;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password", CancellationToken.None);

        var deleteTask = session.DeletePromoCodeAsync(23, CancellationToken.None);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await session.LogoutAsync(CancellationToken.None);
        await session.LoginAsync("contact@example.invalid", "password", CancellationToken.None);
        releaseRequest.TrySetResult();

        var exception = await Assert.ThrowsAsync<ApiException>(() => deleteTask);

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal(2, loginCount);
        Assert.True(session.HasSession);
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new PromoCodeHttpHandler(handler)) { BaseAddress = new Uri("https://opencrm.test/") };

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object value) =>
        new(statusCode) { Content = JsonContent.Create(value) };

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

    private static object CreateProfile() => new
    {
        id = "admin-1",
        email = "contact@example.invalid",
        roles = new[] { "Admin" },
        permissions = new[] { "Admin.Access", "Admin.Configuration.Manage" }
    };

    private static object CreatePromoCode() => new
    {
        id = 7,
        code = "GIFT15",
        name = "Подарок",
        description = "Описание",
        isActive = true,
        validFrom = (DateTime?)null,
        validUntil = (DateTime?)null,
        maximumRedemptions = (int?)null,
        redemptionCount = 0,
        remainingRedemptions = (int?)null,
        demoDurationDays = (int?)null,
        discountTypeCode = "percentage",
        discountValue = 15m,
        discountValidityDays = 30,
        discountPaymentLimitPerUser = (int?)null,
        appliesToAllPaidPlans = false,
        subscriptionPlanIds = new[] { 11, 12 },
        subscriptionPlanNames = new[] { "Бизнес 1", "Бизнес 2" },
        bonusDays = (int?)null,
        maximumOrganizationApplications = 20,
        organizationApplicationCount = 0,
        remainingOrganizationApplications = 20,
        appliesToAllScenarios = false,
        scenarioCodes = new[] { "detailing" },
        giftTemplatePlanId = (int?)null,
        isOrganizationCode = true,
        createdAt = DateTime.UtcNow,
        updatedAt = DateTime.UtcNow
    };

    private sealed class PromoCodeHttpHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}

