using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Services.Interfaces.Imports;
using OpenCRM.WebApp.Controllers.Api.V1.Organizations;

namespace OpenCRM.Modules.Imports.Tests.Integration;

public sealed class OrganizationImportHttpTests
{
    [Fact]
    public async Task EndpointsRequireAuthenticationAndTakeActorFromCurrentIdentity()
    {
        var organizationId = Guid.NewGuid();
        var service = new Mock<IOrganizationImportService>(MockBehavior.Strict);
        service.Setup(imports => imports.CanImportAsync("actor", organizationId)).ReturnsAsync(true);
        using var host = await StartAsync(service.Object);
        using var client = host.GetTestClient();
        var url = $"/api/v1/organizations/{organizationId}/imports/access";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", "actor");

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"canImport\":true", await response.Content.ReadAsStringAsync());
        service.VerifyAll();
    }

    [Fact]
    public async Task ConfirmationRequiresAntiforgeryAndPreservesReviewedRevision()
    {
        var organizationId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var service = new Mock<IOrganizationImportService>(MockBehavior.Strict);
        service.Setup(imports => imports.ConfirmAsync("actor", organizationId, draftId, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportDraftDto { Id = draftId, Revision = 8, ImportedAt = DateTime.UtcNow });
        using var host = await StartAsync(service.Object);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "actor");
        var url = $"/api/v1/organizations/{organizationId}/imports/{draftId}/confirm";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, new { revision = 7 })).StatusCode);
        var tokenResponse = await client.GetAsync("/test/token");
        var token = await tokenResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        client.DefaultRequestHeaders.Add("Cookie", tokenResponse.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
        client.DefaultRequestHeaders.Add("RequestVerificationToken", token!["token"]);

        var response = await client.PostAsJsonAsync(url, new { revision = 7 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var draft = await response.Content.ReadFromJsonAsync<ImportDraftDto>();
        Assert.Equal(draftId, draft!.Id);
        Assert.Equal(8, draft.Revision);
        service.VerifyAll();
    }

    [Fact]
    public async Task ForeignDraftIsNotFoundAndDeniedReportDoesNotLeakData()
    {
        var organizationId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var service = new Mock<IOrganizationImportService>(MockBehavior.Strict);
        service.Setup(imports => imports.GetAsync("actor", organizationId, draftId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        service.Setup(imports => imports.GetReportAsync("actor", organizationId, draftId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());
        using var host = await StartAsync(service.Object);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "actor");
        var url = $"/api/v1/organizations/{organizationId}/imports/{draftId}";

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{url}/report")).StatusCode);
        service.VerifyAll();
    }

    private static async Task<IHost> StartAsync(IOrganizationImportService service)
    {
        return await new HostBuilder().ConfigureWebHost(builder => builder.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton(service);
                services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
                services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { });
                services.AddAuthorization();
                services.AddControllersWithViews().AddApplicationPart(typeof(OrganizationImportsApiController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                    endpoints.MapGet("/test/token", (IAntiforgery antiforgery, HttpContext context) =>
                        Results.Json(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
                });
            })).StartAsync();
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers["X-Test-User"].ToString();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test");

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "test")));
        }
    }
}
