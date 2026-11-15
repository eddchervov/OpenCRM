using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Imports;
using OpenCRM.WebApp.Controllers.Admin.Api;

namespace OpenCRM.Modules.Imports.Tests.Integration;

public sealed class CrmImportHttpTests
{
    [Fact]
    public async Task ConfirmationRequiresPermissionAndUsesAuthenticatedAdministratorAsActor()
    {
        var batchId = Guid.NewGuid();
        var imports = new Mock<ICrmImportService>(MockBehavior.Strict);
        imports.Setup(service => service.ConfirmAsync(batchId, "administrator", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CrmImportResult { ReportId = batchId, CreatedClients = 1 });
        using var host = new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddSingleton(imports.Object);
                services.AddAuthentication("CrmImportTest")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("CrmImportTest", _ => { });
                services.AddAuthorization(options => options.AddPolicy(
                    AdminAuthorizationPolicies.ManagePermissions,
                    policy => policy.RequireAuthenticatedUser().RequireClaim("admin-permission", "manage")));
                services.AddControllers().AddApplicationPart(typeof(CrmImportApiController).Assembly);
            })
            .Configure(application =>
            {
                application.UseRouting();
                application.UseAuthentication();
                application.UseAuthorization();
                application.UseEndpoints(endpoints => endpoints.MapControllers());
            })).Build();

        await host.StartAsync();
        using var client = host.GetTestClient();
        var route = $"/api/v1/admin/crm-import/previews/{batchId:D}/confirm";
        using var anonymous = await client.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", "regular-user");
        using var forbidden = await client.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        using var missingActor = await client.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.Unauthorized, missingActor.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", "blank");
        using var blankActor = await client.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.Unauthorized, blankActor.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User");

        foreach (var claimType in new[] { "UserID", ClaimTypes.NameIdentifier, "sub", "nameid" })
        {
            client.DefaultRequestHeaders.Add("X-Test-User", "administrator");
            client.DefaultRequestHeaders.Add("X-Test-Claim-Type", claimType);
            using var confirmed = await client.PostAsync(route, null);
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
            var result = await confirmed.Content.ReadFromJsonAsync<CrmImportResult>();
            Assert.NotNull(result);
            Assert.Equal(batchId, result.ReportId);
            Assert.Equal(1, result.CreatedClients);
            client.DefaultRequestHeaders.Remove("X-Test-User");
            client.DefaultRequestHeaders.Remove("X-Test-Claim-Type");
        }

        client.DefaultRequestHeaders.Add("X-Test-User", "administrator");
        client.DefaultRequestHeaders.Add("X-Test-Claim-Type", ClaimTypes.NameIdentifier);
        client.DefaultRequestHeaders.Add("X-Test-UserID", "other");
        using var conflictingClaims = await client.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.OK, conflictingClaims.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Remove("X-Test-Claim-Type");
        client.DefaultRequestHeaders.Remove("X-Test-UserID");

        imports.Verify(service => service.ConfirmAsync(batchId, "administrator", It.IsAny<CancellationToken>()), Times.Exactly(5));
        imports.VerifyNoOtherCalls();
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = this.Request.Headers["X-Test-User"].FirstOrDefault();
            var claimType = this.Request.Headers["X-Test-Claim-Type"].FirstOrDefault() ?? ClaimTypes.NameIdentifier;
            var alternateUserId = this.Request.Headers["X-Test-UserID"].FirstOrDefault();
            var isAdministrator = this.Request.Headers["X-Test-Admin"].FirstOrDefault() == "true";
            var hasUserId = this.Request.Headers.ContainsKey("X-Test-User");
            if (!hasUserId && !isAdministrator)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>();
            if (hasUserId)
            {
                var claimValue = userId == "blank" ? " " : userId!;
                claims.Add(new Claim(claimType, claimValue));
            }

            if (!string.IsNullOrWhiteSpace(alternateUserId))
            {
                claims.Add(new Claim("UserID", alternateUserId));
            }

            if (isAdministrator)
            {
                claims.Add(new Claim("admin-permission", "manage"));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, this.Scheme.Name));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, this.Scheme.Name)));
        }
    }
}
