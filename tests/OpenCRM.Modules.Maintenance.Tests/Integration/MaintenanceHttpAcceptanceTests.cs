using OpenCRM.Modules.Planning.Contracts;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using ISystemClock = OpenCRM.Domain.Interfaces.ISystemClock;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Clients;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Defects;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Testing;
using OpenCRM.WebApp.Controllers.Api.V1.Maintenance;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceHttpAcceptanceTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task PersonalOrderUsesModuleThroughHttpAndPreservesAuthorizationAndJson()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        var vehicle = new Vehicle { User = owner, RegistrationPlate = "А123АА154" };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        using var host = this.CreateServer();
        using var client = host.GetTestClient();
        var path = $"/api/v1/vehicles/{vehicle.Id}/maintenance";
        using var anonymous = await client.GetAsync(path + "/1/10");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", owner.Id);

        using var created = await client.PostAsJsonAsync(path, new CreateMaintenanceDto
        {
            Status = MaintenanceStatus.Completed,
            Type = MaintenanceType.Maintenance,
            PlannedDate = null,
            CompletedDate = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc),
            Location = "Пост 1",
            Performer = "Мастер",
            Mileage = 100,
            Comment = "HTTP обслуживание",
            Details = [new CreateMaintenanceDetailDto { Name = "Полировка", Quantity = 1, Price = 1500 }]
        });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = await created.Content.ReadFromJsonAsync<Guid>();
        Assert.NotEqual(Guid.Empty, id);
        using var read = await client.GetAsync($"/api/v1/maintenance/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal("HTTP обслуживание", json.RootElement.GetProperty("comment").GetString());
        Assert.False(json.RootElement.TryGetProperty("Comment", out _));
        using var history = await client.GetAsync($"/api/v1/maintenance/{id}/history");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        using var historyJson = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
        Assert.NotEmpty(historyJson.RootElement.EnumerateArray());
        using var missing = await client.GetAsync($"/api/v1/maintenance/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", "foreign-user");
        using var foreign = await client.GetAsync($"/api/v1/maintenance/{id}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", owner.Id);
        using var deleted = await client.DeleteAsync($"/api/v1/maintenance/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var afterDelete = await client.GetAsync($"/api/v1/maintenance/{id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
        context.ChangeTracker.Clear();
        Assert.True((await context.Maintenance.FindAsync(id))!.IsDeleted);
    }

    [Fact]
    public async Task IntakeDamageHttpProtectsAccessAndReturnsConflictForStaleEdits()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        var organization = new Organization
        {
            OwnerUser = owner, Name = "Осмотр HTTP",
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
        var work = new MaintenanceEntity
        {
            User = owner, Vehicle = new Vehicle { User = owner, Organization = organization },
            Date = DateTime.UtcNow, DateTimeCreation = DateTime.UtcNow, Status = MaintenanceStatus.Planned,
            Details = [new() { Name = "Полировка" }]
        };
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var host = this.CreateServer();
        using var client = host.GetTestClient();
        var path = $"/api/v1/maintenance/{work.Id}/intake/damages?organizationId={organization.Id}";
        using var anonymous = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", owner.Id);
        var snapshot = await client.GetFromJsonAsync<MaintenanceIntakeDamagesDto>(path);
        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.Damages);
        var damageId = Guid.NewGuid();
        var mutationPath = $"/api/v1/maintenance/{work.Id}/intake/damages/{damageId}?organizationId={organization.Id}";
        var request = new SaveMaintenanceIntakeDamageDto
        {
            DiagramVersion = MaintenanceIntakeDiagramVersions.SedanV1,
            Version = snapshot.Version, ViewCode = "front", DamageType = "chip", X = 0.2, Y = 0.4, Description = "Скол на бампере"
        };
        using var saved = await client.PutAsJsonAsync(mutationPath, request);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using var stale = await client.PutAsJsonAsync(mutationPath, request);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var updated = await client.GetFromJsonAsync<MaintenanceIntakeDamagesDto>(path);
        Assert.Equal(damageId, Assert.Single(updated!.Damages).Id);
        using var staleDelete = await client.DeleteAsync(mutationPath + $"&version={snapshot.Version}");
        Assert.Equal(HttpStatusCode.Conflict, staleDelete.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", "foreign-user");
        using var foreign = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", owner.Id);
        using var deleted = await client.DeleteAsync(mutationPath + $"&version={updated.Version}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    private IHost CreateServer()
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc));
        var builder = new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddScoped<OpenCRMContext>(_ => fixture.CreateContext());
            services.AddScoped<IMaintenanceRepository, MaintenanceRepository>();
            services.AddScoped<IVehicleRepository, VehicleRepository>();
            services.AddSingleton(Mock.Of<IMaintenancePhotoRepository>());
            services.AddSingleton(Mock.Of<IVehicleCounterCommandService>());
            services.AddSingleton(Mock.Of<IMaintenanceConsumptionService>());
            services.AddSingleton(clock.Object);
            var subscriptions = new Mock<IOrganizationSubscriptionService>();
            subscriptions.Setup(service => service.EnsureWorkspaceCanReadAsync("foreign-user", It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Нет доступа к организации."));
            subscriptions.Setup(service => service.HasFeatureAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), Helpers.FeatureCodes.MaintenanceIntake))
                .ReturnsAsync(true);
            subscriptions.Setup(service => service.HasFeatureAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), Helpers.FeatureCodes.VehiclePhotoAlbums))
                .ReturnsAsync(true);
            services.AddSingleton(subscriptions.Object);
            services.AddSingleton(Mock.Of<IOrganizationPermissionService>());
            services.AddSingleton(Mock.Of<IMaintenanceSchedulerSyncService>());
            services.AddSingleton(Mock.Of<IOrganizationInquiryOriginReader>());
            services.AddSingleton(Mock.Of<IVehicleDefectService>());
            services.AddScoped<OpenCRM.Modules.Vehicles.Contracts.IVehicleMaintenanceLookupService, OpenCRM.Modules.Vehicles.VehicleMaintenanceLookupService>();
            services.AddOpenCRMMaintenance();
            services.AddOpenCRMClients();
            services.AddAuthentication("MaintenanceTest")
                .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("MaintenanceTest", _ => { });
            services.AddAuthorization();
            services.AddControllers().AddApplicationPart(typeof(MaintenanceApiController).Assembly);
        }).Configure(application =>
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

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = this.Request.Headers["X-Test-User"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], this.Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
