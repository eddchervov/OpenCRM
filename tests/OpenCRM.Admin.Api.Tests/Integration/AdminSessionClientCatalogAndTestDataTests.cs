using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenCRM.Admin.Api;

namespace OpenCRM.Admin.Api.Tests.Integration;

public sealed class AdminSessionClientCatalogAndTestDataTests
{
    [Fact]
    public async Task CatalogAndSuggestionOperationsUseExpectedRoutesAndBodies()
    {
        var requests = new List<(string Method, string Path, string? Body)>();
        using var http = CreateClient(async (request, _) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());

            requests.Add((request.Method.Method, path, request.Content == null ? null : await request.Content.ReadAsStringAsync()));
            return path switch
            {
                "/api/v1/admin/catalog/brands?kind=3&search=yamaha%20motor" => JsonResponse(HttpStatusCode.OK, new[]
                {
                    new { id = 12, name = "Yamaha Motor", kind = 3, kindText = "Мотоцикл", modelsCount = 4 }
                }),
                "/api/v1/admin/catalog/brands/12/models?search=mt" => JsonResponse(HttpStatusCode.OK, new[]
                {
                    new { id = 14, brandId = 12, name = "MT-07", vehiclesCount = 2 }
                }),
                "/api/v1/admin/catalog-suggestions?status=1" => JsonResponse(HttpStatusCode.OK, new[]
                {
                    new { id = 3, kind = 3, kindText = "Мотоцикл", brandName = "Yamaha", modelName = "MT-09", status = 1, statusText = "На модерации" }
                }),
                "/api/v1/admin/catalog/seed" => JsonResponse(HttpStatusCode.OK, new
                {
                    createdBrands = 2, createdModels = 5, skippedDeletedBrands = 1, skippedDeletedModels = 3
                }),
                _ => JsonResponse(HttpStatusCode.OK, new { message = "Операция выполнена.", id = 23 })
            };
        });
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password");

        var brands = await session.GetCatalogBrandsAsync(3, "yamaha motor");
        var models = await session.GetCatalogModelsAsync(12, "mt");
        var suggestions = await session.GetCatalogSuggestionsAsync(1);
        await session.SaveCatalogBrandAsync("Suzuki", 3);
        await session.SaveCatalogBrandAsync("Suzuki Motor", 3, 23);
        await session.DeleteCatalogBrandAsync(23);
        await session.SaveCatalogModelAsync(12, "MT-09");
        await session.SaveCatalogModelAsync(12, "MT-09 SP", 14);
        await session.DeleteCatalogModelAsync(14);
        await session.ReviewCatalogSuggestionAsync(3, true, "Проверено");
        await session.ReviewCatalogSuggestionAsync(3, false, "Дубль");
        var seeded = await session.SeedVehicleCatalogAsync();

        Assert.Equal("Yamaha Motor", Assert.Single(brands).Name);
        Assert.Equal("MT-07", Assert.Single(models).Name);
        Assert.Equal("На модерации", Assert.Single(suggestions).StatusText);
        Assert.Equal((2, 5, 1, 3), (seeded.CreatedBrands, seeded.CreatedModels, seeded.SkippedDeletedBrands, seeded.SkippedDeletedModels));
        Assert.Equal(new[]
        {
            ("GET", "/api/v1/admin/catalog/brands?kind=3&search=yamaha%20motor"),
            ("GET", "/api/v1/admin/catalog/brands/12/models?search=mt"),
            ("GET", "/api/v1/admin/catalog-suggestions?status=1"),
            ("POST", "/api/v1/admin/catalog/brands"),
            ("PUT", "/api/v1/admin/catalog/brands/23"),
            ("DELETE", "/api/v1/admin/catalog/brands/23"),
            ("POST", "/api/v1/admin/catalog/brands/12/models"),
            ("PUT", "/api/v1/admin/catalog/models/14"),
            ("DELETE", "/api/v1/admin/catalog/models/14"),
            ("POST", "/api/v1/admin/catalog-suggestions/3/approve"),
            ("POST", "/api/v1/admin/catalog-suggestions/3/reject"),
            ("POST", "/api/v1/admin/catalog/seed")
        }, requests.Select(request => (request.Method, request.Path)));
        AssertBodyProperty(requests[3].Body, "name", "Suzuki");
        AssertBodyProperty(requests[3].Body, "kind", 3);
        AssertBodyProperty(requests[6].Body, "name", "MT-09");
        AssertBodyProperty(requests[9].Body, "comment", "Проверено");
        AssertBodyProperty(requests[10].Body, "comment", "Дубль");
        Assert.Null(requests[^1].Body);
    }

    [Fact]
    public async Task TestDataOperationsKeepOrganizationVehicleAndGenerationParameters()
    {
        var organizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var vehicleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var requests = new List<(string Method, string Path, string? Body)>();
        using var http = CreateClient(async (request, _) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            if (path == "/api/v1/admin/current-user") return JsonResponse(HttpStatusCode.OK, CreateProfile());
            requests.Add((request.Method.Method, path, request.Content == null ? null : await request.Content.ReadAsStringAsync()));
            if (path == $"/api/v1/admin/test-data/organizations/{organizationId}/vehicles")
            {
                return JsonResponse(HttpStatusCode.OK, new[]
                {
                    new { id = vehicleId, kind = 1, displayName = "Toyota Camry", brandName = "Toyota", modelName = "Camry", maintenanceCount = 3, defectCount = 1, organizationId }
                });
            }

            return path == "/api/v1/admin/test-data/organizations?search=service%20one"
                ? JsonResponse(HttpStatusCode.OK, new[]
                {
                    new { id = organizationId, name = "Service One", ownerUserId = "owner", ownerEmail = "contact@example.invalid", scenarioCode = "sto", isActive = true, vehiclesCount = 1, maintenanceCount = 3 }
                })
                : JsonResponse(HttpStatusCode.OK, new { message = "Создано.", createdCount = 10 });
        });
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password");

        var organizations = await session.GetTestDataOrganizationsAsync("service one");
        var vehicles = await session.GetTestDataVehiclesAsync(organizationId);
        await session.GenerateTestVehiclesAsync(organizationId, 10);
        await session.GenerateTestMaintenancesAsync(organizationId, vehicleId, 20, "planned");
        await session.GenerateTestDefectsAsync(organizationId, vehicleId, 6);

        Assert.Equal(organizationId, Assert.Single(organizations).Id);
        Assert.Equal(vehicleId, Assert.Single(vehicles).Id);
        Assert.Equal(new[]
        {
            ("GET", $"/api/v1/admin/test-data/organizations?search=service%20one"),
            ("GET", $"/api/v1/admin/test-data/organizations/{organizationId}/vehicles"),
            ("POST", $"/api/v1/admin/test-data/organizations/{organizationId}/vehicles/generate"),
            ("POST", $"/api/v1/admin/test-data/organizations/{organizationId}/vehicles/{vehicleId}/maintenances/generate"),
            ("POST", $"/api/v1/admin/test-data/organizations/{organizationId}/vehicles/{vehicleId}/defects/generate")
        }, requests.Select(request => (request.Method, request.Path)));
        AssertBodyProperty(requests[2].Body, "count", 10);
        AssertBodyProperty(requests[3].Body, "count", 20);
        AssertBodyProperty(requests[3].Body, "mode", "planned");
        AssertBodyProperty(requests[4].Body, "count", 6);
    }

    [Fact]
    public async Task TestDataGenerationRejectsCountsAndModesOutsideServerLimits()
    {
        using var http = CreateClient((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/auth/login") return JsonResponse(HttpStatusCode.OK, CreateTokens());
            return request.RequestUri.AbsolutePath == "/api/v1/admin/current-user"
                ? JsonResponse(HttpStatusCode.OK, CreateProfile())
                : throw new InvalidOperationException("Недопустимый запрос не должен отправляться.");
        });
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password");
        var organizationId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.GenerateTestVehiclesAsync(organizationId, 101));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.GenerateTestDefectsAsync(organizationId, vehicleId, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.GenerateTestMaintenancesAsync(organizationId, vehicleId, 301, "planned"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.GenerateTestMaintenancesAsync(organizationId, vehicleId, 1, "random"));
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new TestHandler(handler)) { BaseAddress = new Uri("https://opencrm.test/") };

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) =>
        CreateClient((request, token) => Task.FromResult(handler(request, token)));

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) =>
        new(status) { Content = JsonContent.Create(body) };

    private static void AssertBodyProperty(string? body, string property, string expected)
    {
        using var json = JsonDocument.Parse(body!);
        Assert.Equal(expected, json.RootElement.GetProperty(property).GetString());
    }

    private static void AssertBodyProperty(string? body, string property, int expected)
    {
        using var json = JsonDocument.Parse(body!);
        Assert.Equal(expected, json.RootElement.GetProperty(property).GetInt32());
    }

    private static object CreateTokens()
    {
        var now = DateTimeOffset.UtcNow;
        return new
        {
            accessToken = "access-token", accessTokenExpiresAt = now.AddHours(1),
            refreshToken = "refresh-token", refreshTokenExpiresAt = now.AddDays(10), tokenType = "Bearer",
            user = new { id = "admin-1", email = "contact@example.invalid", userName ="admin", roles = new[] { "Admin" } }
        };
    }

    private static object CreateProfile() => new
    {
        id = "admin-1", email = "contact@example.invalid", roles = new[] { "Admin" }, permissions = new[] { "Admin.Access" }
    };

    private sealed class TestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
