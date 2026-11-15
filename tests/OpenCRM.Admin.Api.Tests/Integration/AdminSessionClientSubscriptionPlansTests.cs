using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api.Tests.Integration;

public sealed class AdminSessionClientSubscriptionPlansTests
{
    [Fact]
    public async Task PlanCrudAndFeatureOperationsUseAdminRoutesAndRoundTripAllValues()
    {
        var requests = new List<(string Method, string Path, string? Body)>();
        using var http = AdminSessionClientSubscriptionPlansTests.CreateClient(async (request, _) =>
        {
            var path = request.RequestUri!.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
            if (path == "api/v1/auth/login") return AdminSessionClientSubscriptionPlansTests.Json(HttpStatusCode.OK, AdminSessionClientSubscriptionPlansTests.CreateTokens());
            if (path == "api/v1/admin/current-user") return AdminSessionClientSubscriptionPlansTests.Json(HttpStatusCode.OK, AdminSessionClientSubscriptionPlansTests.CreateProfile());

            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            requests.Add((request.Method.Method, path, body));
            return path switch
            {
                "api/v1/admin/subscription-plans" when request.Method == HttpMethod.Get =>
                    AdminSessionClientSubscriptionPlansTests.Json(HttpStatusCode.OK, new[]
                    {
                        new { id = 3, name = "Тариф", description = "Описание", price = 1500m, discountPrice = 1200m,
                            effectivePrice = 1200m, hasDiscount = true, durationDays = 30, isActive = true,
                            vehicleLimit = 15, order = 2, group = "Основные", planTypeCode = "business",
                            planTypeName = "Организационный", isDemoPlan = false, isDefaultPlan = false,
                            hasUsageHistory = false, canDelete = true }
                    }),
                "api/v1/admin/subscription-plans/3/features" when request.Method == HttpMethod.Get =>
                    AdminSessionClientSubscriptionPlansTests.Json(HttpStatusCode.OK, new[]
                    {
                        new { featureId = 12, code = "multi_user", name = "Участники", description = "Доступ участникам",
                            category = "Работа", scopeCode = "organization", scopeName = "Организация", audienceCode = "business",
                            audienceName = "Бизнес", valueType = "Boolean", isAssigned = true, boolValue = (bool?)true, intValue = (int?)null },
                        new { featureId = 13, code = "vehicle_limit", name = "Лимит", description = "Количество техники",
                            category = "Ограничения", scopeCode = "organization", scopeName = "Организация", audienceCode = "business",
                            audienceName = "Бизнес", valueType = "Integer", isAssigned = true, boolValue = (bool?)null, intValue = (int?)15 }
                    }),
                "api/v1/admin/subscription-plans/3/toggle-status" =>
                    AdminSessionClientSubscriptionPlansTests.Json(HttpStatusCode.OK, new { message = "Статус тарифа обновлён." }),
                "api/v1/admin/subscription-plans/3/features" =>
                    AdminSessionClientSubscriptionPlansTests.Json(HttpStatusCode.OK, new { message = "Функции сохранены для всех сроков оплаты." }),
                "api/v1/admin/subscription-plans/3" when request.Method == HttpMethod.Put =>
                    AdminSessionClientSubscriptionPlansTests.Json(HttpStatusCode.OK, new { id = 3, name = "Тариф" }),
                "api/v1/admin/subscription-plans/3" when request.Method == HttpMethod.Delete =>
                    AdminSessionClientSubscriptionPlansTests.Json(HttpStatusCode.OK, new { message = "Тариф удалён." }),
                "api/v1/admin/subscription-plans" when request.Method == HttpMethod.Post =>
                    AdminSessionClientSubscriptionPlansTests.Json(HttpStatusCode.Created, new { id = 3, name = "Тариф" }),
                _ => throw new InvalidOperationException($"Неожиданный запрос {request.Method} {path}.")
            };
        });
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password");

        var plans = await session.GetSubscriptionPlansAsync();
        var created = await session.CreateSubscriptionPlanAsync(AdminSessionClientSubscriptionPlansTests.Form());
        var updated = await session.UpdateSubscriptionPlanAsync(3, AdminSessionClientSubscriptionPlansTests.Form());
        var toggled = await session.ToggleSubscriptionPlanAsync(3);
        var features = await session.GetPlanFeaturesAsync(3);
        features[0].BoolValue = false;
        features[1].IntValue = 25;
        var savedFeatures = await session.SavePlanFeaturesAsync(3, features);
        var deleted = await session.DeleteSubscriptionPlanAsync(3);

        Assert.Equal("business", Assert.Single(plans).PlanTypeCode);
        Assert.Equal(1200m, plans[0].EffectivePrice);
        Assert.True(plans[0].CanDelete);
        Assert.Equal(3, created.Id);
        Assert.Equal(3, updated.Id);
        Assert.Equal("Статус тарифа обновлён.", toggled.Message);
        Assert.Equal("Integer", features[1].ValueType);
        Assert.Equal("Функции сохранены для всех сроков оплаты.", savedFeatures.Message);
        Assert.Equal("Тариф удалён.", deleted.Message);
        Assert.Equal(new[]
        {
            ("GET", "api/v1/admin/subscription-plans"),
            ("POST", "api/v1/admin/subscription-plans"),
            ("PUT", "api/v1/admin/subscription-plans/3"),
            ("PATCH", "api/v1/admin/subscription-plans/3/toggle-status"),
            ("GET", "api/v1/admin/subscription-plans/3/features"),
            ("PUT", "api/v1/admin/subscription-plans/3/features"),
            ("DELETE", "api/v1/admin/subscription-plans/3")
        }, requests.Select(request => (request.Method, request.Path)));

        using var createBody = JsonDocument.Parse(requests[1].Body!);
        Assert.Equal("business", createBody.RootElement.GetProperty("planTypeCode").GetString());
        Assert.True(createBody.RootElement.GetProperty("isDemoPlan").GetBoolean());
        Assert.Equal(30, createBody.RootElement.GetProperty("durationDays").GetInt32());

        using var featuresBody = JsonDocument.Parse(requests[5].Body!);
        var featureValues = featuresBody.RootElement.GetProperty("features").EnumerateArray().ToArray();
        Assert.False(featureValues[0].GetProperty("boolValue").GetBoolean());
        Assert.Equal(25, featureValues[1].GetProperty("intValue").GetInt32());
        Assert.True(featureValues[0].GetProperty("isAssigned").GetBoolean());
    }

    [Fact]
    public async Task UpdateAndFeatureCallsRejectInvalidIdsBeforeSending()
    {
        using var http = AdminSessionClientSubscriptionPlansTests.CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => AdminSessionClientSubscriptionPlansTests.Json(HttpStatusCode.OK, AdminSessionClientSubscriptionPlansTests.CreateTokens()),
            "/api/v1/admin/current-user" => AdminSessionClientSubscriptionPlansTests.Json(HttpStatusCode.OK, AdminSessionClientSubscriptionPlansTests.CreateProfile()),
            _ => throw new InvalidOperationException("Запрос не должен отправляться.")
        });
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            session.UpdateSubscriptionPlanAsync(0, AdminSessionClientSubscriptionPlansTests.Form()));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.GetPlanFeaturesAsync(-1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.DeleteSubscriptionPlanAsync(0));
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new FakeHttpMessageHandler(handler)) { BaseAddress = new Uri("https://opencrm.test/") };

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) =>
        AdminSessionClientSubscriptionPlansTests.CreateClient((request, token) => Task.FromResult(handler(request, token)));

    private static HttpResponseMessage Json(HttpStatusCode status, object content) =>
        new(status) { Content = JsonContent.Create(content) };

    private static AdminSubscriptionPlanForm Form() => new()
    {
        Name = "Тариф",
        Description = "Описание",
        Price = 1500,
        DiscountPrice = 1200,
        DurationDays = 30,
        VehicleLimit = 15,
        Order = 2,
        Group = "Основные",
        PlanTypeCode = "business",
        IsActive = true,
        IsDemoPlan = true
    };

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
        permissions = new[] { "Admin.Access", "Admin.Configuration.Manage" }
    };

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
