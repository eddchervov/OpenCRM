using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using ISystemClock = OpenCRM.Domain.Interfaces.ISystemClock;
using OpenCRM.Domain.Interfaces.Catalog;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Domain.Interfaces.Models;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.WebApp.Controllers.Api.V1.Vehicles;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehicleHttpAcceptanceTests(VehicleDatabaseFixture fixture) : IClassFixture<VehicleDatabaseFixture>
{
    private const string AuthenticationScheme = "VehiclesTest";

    [Fact]
    public async Task VehicleHttpFlowPreservesJsonContractAndEnforcesIdentityWorkspacePermissionsAndLimits()
    {
        await using var seedContext = fixture.CreateContext();
        var owner = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = "vehicle-owner@example.test" };
        var viewer = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = "vehicle-viewer@example.test" };
        var outsider = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = "vehicle-outsider@example.test" };
        var limited = new ApplicationUser { Id = Guid.NewGuid().ToString(), Email = "vehicle-limited@example.test" };
        var organization = new Organization { Name = "HTTP организация", OwnerUser = owner, OwnerUserId = owner.Id };
        var client = new OrganizationClient { Organization = organization, OrganizationId = organization.Id, Name = "HTTP клиент" };
        seedContext.Users.AddRange(owner, viewer, outsider, limited);
        seedContext.Vehicles.Add(new Vehicle { User = limited, Name = "Первый автомобиль" });
        seedContext.Organizations.Add(organization);
        seedContext.OrganizationClients.Add(client);
        await seedContext.SaveChangesAsync();

        var organizationAccess = new Mock<IOrganizationSubscriptionService>();
        organizationAccess
            .Setup(service => service.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), organization.Id, It.IsAny<string>()))
            .Callback<string, Guid, string>((userId, _, _) =>
            {
                if (userId == outsider.Id)
                {
                    throw new InvalidOperationException("Нет доступа к организации.");
                }
            })
            .Returns(Task.CompletedTask);
        organizationAccess
            .Setup(service => service.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), organization.Id, It.IsAny<string>()))
            .Callback<string, Guid, string>((userId, _, _) =>
            {
                if (userId == viewer.Id)
                {
                    throw new InvalidOperationException("Для пользователя доступ только для чтения.");
                }
            })
            .Returns(Task.CompletedTask);
        organizationAccess
            .Setup(service => service.GetVehicleLimitAsync(It.IsAny<string>(), organization.Id, It.IsAny<int>()))
            .ReturnsAsync(10);

        var featureAccess = new Mock<IFeatureAccessService>();
        featureAccess
            .Setup(service => service.GetIntValueAsync(It.IsAny<string>(), Helpers.FeatureCodes.VehiclesLimit, It.IsAny<int>()))
            .ReturnsAsync(10);
        featureAccess
            .Setup(service => service.GetIntValueAsync(limited.Id, Helpers.FeatureCodes.VehiclesLimit, It.IsAny<int>()))
            .ReturnsAsync(1);
        var subscriptionService = new Mock<IPersonalSubscriptionQueryService>();
        subscriptionService.Setup(service => service.HasActiveSubscriptionAsync(It.IsAny<string>())).ReturnsAsync(false);
        var photoRepository = new Mock<IVehiclePhotoRepository>();
        photoRepository
            .Setup(repository => repository.GetByVehicleIdsAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<IEnumerable<Guid>>(), false))
            .ReturnsAsync(new List<VehiclePhoto>());
        photoRepository
            .Setup(repository => repository.GetByVehicleIdAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), false))
            .ReturnsAsync((VehiclePhoto)null!);
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(systemClock => systemClock.UtcNow).Returns(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc));

        using var server = VehicleHttpAcceptanceTests.CreateServer(
            fixture,
            organizationAccess.Object,
            featureAccess.Object,
            subscriptionService.Object,
            photoRepository.Object,
            clock.Object);
        using var anonymousClient = server.GetTestClient();
        using var ownerClient = VehicleHttpAcceptanceTests.CreateAuthenticatedClient(server, owner.Id);
        using var viewerClient = VehicleHttpAcceptanceTests.CreateAuthenticatedClient(server, viewer.Id);
        using var outsiderClient = VehicleHttpAcceptanceTests.CreateAuthenticatedClient(server, outsider.Id);
        using var limitedClient = VehicleHttpAcceptanceTests.CreateAuthenticatedClient(server, limited.Id);

        var anonymousResponse = await anonymousClient.GetAsync("/api/v1/vehicles");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        var createPersonalResponse = await ownerClient.PostAsJsonAsync("/api/v1/vehicles", new CreateVehicleDto
        {
            Name = "Личная Toyota",
            ManualBrandName = "Toyota",
            ManualModelName = "Camry",
            RegistrationPlate = "А123АА154"
        });
        Assert.Equal(HttpStatusCode.OK, createPersonalResponse.StatusCode);
        var personalVehicleId = await createPersonalResponse.Content.ReadFromJsonAsync<Guid>();
        Assert.NotEqual(Guid.Empty, personalVehicleId);

        var personalGetResponse = await ownerClient.GetAsync($"/api/v1/vehicles/{personalVehicleId:D}");
        Assert.Equal(HttpStatusCode.OK, personalGetResponse.StatusCode);
        using var personalJson = JsonDocument.Parse(await personalGetResponse.Content.ReadAsStringAsync());
        var personalVehicleJson = personalJson.RootElement;
        Assert.Equal(personalVehicleId, personalVehicleJson.GetProperty("id").GetGuid());
        Assert.Equal("Toyota", personalVehicleJson.GetProperty("brandName").GetString());
        Assert.Equal("Camry", personalVehicleJson.GetProperty("modelName").GetString());
        Assert.Equal("А123АА154", personalVehicleJson.GetProperty("registrationPlate").GetString());
        Assert.False(personalVehicleJson.GetProperty("isArchived").GetBoolean());
        Assert.True(personalVehicleJson.GetProperty("isManualEntry").GetBoolean());
        Assert.True(personalVehicleJson.TryGetProperty("organizationId", out var organizationIdJson));
        Assert.Equal(JsonValueKind.Null, organizationIdJson.ValueKind);
        Assert.False(personalVehicleJson.TryGetProperty("BrandName", out _));

        var personalListResponse = await ownerClient.GetAsync("/api/v1/vehicles?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, personalListResponse.StatusCode);
        using var listJson = JsonDocument.Parse(await personalListResponse.Content.ReadAsStringAsync());
        var listRoot = listJson.RootElement;
        Assert.Equal(personalVehicleId, listRoot.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(1, listRoot.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, listRoot.GetProperty("allCount").GetInt32());
        Assert.Equal(1, listRoot.GetProperty("page").GetInt32());
        Assert.Equal(10, listRoot.GetProperty("pageSize").GetInt32());
        Assert.True(listRoot.TryGetProperty("userBrands", out _));
        Assert.True(listRoot.TryGetProperty("userKinds", out _));

        var updateResponse = await ownerClient.PutAsJsonAsync($"/api/v1/vehicles/{personalVehicleId:D}", new UpdateVehicleDto
        {
            Name = "Личная Toyota обновлена",
            ManualBrandName = "Toyota",
            ManualModelName = "Camry",
            RegistrationPlate = "А123АА154"
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        using var updatedJson = JsonDocument.Parse(await (await ownerClient.GetAsync($"/api/v1/vehicles/{personalVehicleId:D}")).Content.ReadAsStringAsync());
        Assert.Equal("Личная Toyota обновлена", updatedJson.RootElement.GetProperty("name").GetString());

        var archiveResponse = await ownerClient.DeleteAsync($"/api/v1/vehicles/{personalVehicleId:D}");
        Assert.Equal(HttpStatusCode.NoContent, archiveResponse.StatusCode);
        var archivedListResponse = await ownerClient.GetAsync("/api/v1/vehicles?isArchived=true&page=1&pageSize=10");
        using var archivedListJson = JsonDocument.Parse(await archivedListResponse.Content.ReadAsStringAsync());
        Assert.Equal(personalVehicleId, archivedListJson.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
        var restoreResponse = await ownerClient.PostAsync($"/api/v1/vehicles/{personalVehicleId:D}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
        var countsResponse = await ownerClient.GetAsync("/api/v1/vehicles/counts");
        using var countsJson = JsonDocument.Parse(await countsResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, countsJson.RootElement.GetProperty("activeCount").GetInt32());
        Assert.Equal(0, countsJson.RootElement.GetProperty("archivedCount").GetInt32());

        var createOrganizationResponse = await ownerClient.PostAsJsonAsync("/api/v1/vehicles", new CreateVehicleDto
        {
            OrganizationId = organization.Id,
            OrganizationClientId = client.Id,
            Name = "Рабочий Урал",
            ManualBrandName = "Урал",
            ManualModelName = "Next"
        });
        Assert.Equal(HttpStatusCode.OK, createOrganizationResponse.StatusCode);
        var organizationVehicleId = await createOrganizationResponse.Content.ReadFromJsonAsync<Guid>();
        var organizationGetPath = $"/api/v1/vehicles/{organizationVehicleId:D}?organizationId={organization.Id:D}";
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync(organizationGetPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewerClient.GetAsync(organizationGetPath)).StatusCode);
        using var organizationJson = JsonDocument.Parse(await (await ownerClient.GetAsync(organizationGetPath)).Content.ReadAsStringAsync());
        Assert.Equal(organization.Id, organizationJson.RootElement.GetProperty("organizationId").GetGuid());
        Assert.Equal(client.Id, organizationJson.RootElement.GetProperty("organizationClientId").GetGuid());
        Assert.Equal(client.Name, organizationJson.RootElement.GetProperty("organizationClientName").GetString());

        var readonlyUpdateResponse = await viewerClient.PutAsJsonAsync(
            $"/api/v1/vehicles/{organizationVehicleId:D}?organizationId={organization.Id:D}",
            new UpdateVehicleDto { Name = "Запрещенное изменение" });
        Assert.Equal(HttpStatusCode.Forbidden, readonlyUpdateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsiderClient.GetAsync(organizationGetPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync($"/api/v1/vehicles/{personalVehicleId:D}")).StatusCode);

        var limitResponse = await limitedClient.PostAsJsonAsync("/api/v1/vehicles", new CreateVehicleDto { Name = "Превышение лимита" });
        Assert.Equal((HttpStatusCode)402, limitResponse.StatusCode);
        using var limitJson = JsonDocument.Parse(await limitResponse.Content.ReadAsStringAsync());
        Assert.Contains("лимит", limitJson.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static IHost CreateServer(
        VehicleDatabaseFixture fixture,
        IOrganizationSubscriptionService organizationSubscriptionService,
        IFeatureAccessService featureAccessService,
        IPersonalSubscriptionQueryService subscriptionService,
        IVehiclePhotoRepository vehiclePhotoRepository,
        ISystemClock systemClock)
    {
        var builder = new HostBuilder()
            .ConfigureAppConfiguration(config => config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "vehicle-appsettings.json")))
            .ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddScoped<OpenCRMContext>(_ => fixture.CreateContext());
                services.AddScoped<IVehicleRepository, VehicleRepository>();
                services.AddScoped<IModelRepository, ModelRepository>();
                services.AddScoped<IVehicleCatalogSuggestionRepository, VehicleCatalogSuggestionRepository>();
                services.AddSingleton(organizationSubscriptionService);
                services.AddSingleton(Mock.Of<IOrganizationPermissionService>());
                services.AddSingleton(featureAccessService);
                services.AddSingleton(Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == true));
                services.AddSingleton(subscriptionService);
                services.AddSingleton(vehiclePhotoRepository);
                services.AddSingleton(systemClock);
                services.AddOpenCRMVehicles();
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = VehicleHttpAcceptanceTests.AuthenticationScheme;
                    options.DefaultChallengeScheme = VehicleHttpAcceptanceTests.AuthenticationScheme;
                    options.DefaultForbidScheme = VehicleHttpAcceptanceTests.AuthenticationScheme;
                }).AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(
                    VehicleHttpAcceptanceTests.AuthenticationScheme,
                    _ => { });
                services.AddAuthorization();
                services.AddControllers().AddApplicationPart(typeof(VehicleApiController).Assembly);
            })
            .Configure(application =>
            {
                application.UseRouting();
                application.UseAuthentication();
                application.UseAuthorization();
                application.UseEndpoints(endpoints => endpoints.MapControllers());
            }));

        var host = builder.Build();
        host.Start();

        return host;
    }

    private static HttpClient CreateAuthenticatedClient(IHost server, string userId)
    {
        var client = server.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", userId);

        return client;
    }

    private sealed class HeaderAuthenticationHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = this.Request.Headers["X-Test-User"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim("UserID", userId)
            };
            var identity = new ClaimsIdentity(claims, this.Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, this.Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
