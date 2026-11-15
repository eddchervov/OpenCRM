using Microsoft.AspNetCore.Http;
using OpenCRM.Modules.Catalog;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.WebApp.Controllers.Api.V1.Organizations;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationHttpAcceptanceTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task DataExportQueuesForOwnerAndRequiresOwner()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var employee = await OrganizationCommandHarness.AddMemberAsync(context, organization);
        var archives = new Dictionary<string, byte[]>();
        var storage = new Mock<IFileStorageService>();
        storage.Setup(value => value.SavePrivateFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<string, Stream, CancellationToken>(async (path, stream, token) =>
            {
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, token);
                archives[path] = memory.ToArray();

                return path;
            });
        storage.Setup(value => value.GetPrivateFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((path, _) => archives.TryGetValue(path, out var bytes)
                ? Task.FromResult<Stream>(new MemoryStream(bytes))
                : Task.FromException<Stream>(new FileNotFoundException()));
        await context.Set<OrganizationDataExportJob>().ExecuteDeleteAsync();
        using var host = this.CreateHost(storage.Object);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Identity-Claim", "UserID");
        var route = $"/api/v1/organizations/{organization.Id}/data-export";
        using var anonymous = await client.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", employee.UserId);
        await AddAntiforgeryTokenAsync(client);
        using var forbidden = await client.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var forbiddenLegacy = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenLegacy.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        await AddAntiforgeryTokenAsync(client);
        using var response = await client.PostAsync(route, null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var requested = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var jobId = requested.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("queued", requested.RootElement.GetProperty("state").GetString());
        using var statusResponse = await client.GetAsync(route + "/status");
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        Assert.Equal(jobId, status.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("queued", status.RootElement.GetProperty("state").GetString());

        using var legacyRedirect = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Redirect, legacyRedirect.StatusCode);
        Assert.Equal(
            $"/detailing/{organization.Id:D}#settings?tab=export",
            legacyRedirect.Headers.Location?.OriginalString);
        Assert.True(legacyRedirect.Headers.CacheControl?.NoStore);
        using var queuedStatusResponse = await client.GetAsync(route + "/status");
        using var queuedStatus = JsonDocument.Parse(await queuedStatusResponse.Content.ReadAsStringAsync());
        Assert.Equal(jobId, queuedStatus.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("queued", queuedStatus.RootElement.GetProperty("state").GetString());

        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IOrganizationDataExportService>().ProcessPendingAsync(CancellationToken.None);
        using var downloaded = await client.GetAsync(route + $"/{jobId}/file");
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal("application/zip", downloaded.Content.Headers.ContentType?.MediaType);
        var bytes = await downloaded.Content.ReadAsByteArrayAsync();
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        using var manifest = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
        Assert.Equal(organization.Id, manifest.RootElement.GetProperty("organizationId").GetGuid());
        using var legacyDownloaded = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, legacyDownloaded.StatusCode);
        Assert.Equal("application/zip", legacyDownloaded.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await legacyDownloaded.Content.ReadAsByteArrayAsync());
        using var repeated = await client.GetAsync(route + $"/{jobId}/file");
        Assert.Equal(bytes, await repeated.Content.ReadAsByteArrayAsync());
        archives.Clear();
        using var missingLegacyArchive = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Redirect, missingLegacyArchive.StatusCode);
        Assert.Equal(
            $"/detailing/{organization.Id:D}#settings?tab=export",
            missingLegacyArchive.Headers.Location?.OriginalString);
        storage.Verify(value => value.SavePrivateFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmployeeEndpointEnforcesIdentityAndHidesPhoneAndMemberLinksWithoutPermission()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var operatorMember = await OrganizationCommandHarness.AddMemberAsync(context, organization);
        var viewer = await OrganizationCommandHarness.AddMemberAsync(context, organization, OrganizationRoleCodes.Viewer);
        var employee = new OrganizationEmployee
        {
            Organization = organization, Name = "Мастер", Phone = "+7 900 0000000",
            MemberId = operatorMember.Id
        };
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        var route = $"/api/v1/organizations/{organization.Id}/employees";
        using var anonymous = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", viewer.UserId);
        using var forbidden = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", operatorMember.UserId);
        using var limited = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, limited.StatusCode);
        using var limitedJson = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        var limitedItem = Assert.Single(limitedJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(employee.Id, limitedItem.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, limitedItem.GetProperty("phone").ValueKind);
        Assert.Equal(JsonValueKind.Null, limitedItem.GetProperty("memberId").ValueKind);
        Assert.False(limitedJson.RootElement.GetProperty("canManage").GetBoolean());
        Assert.False(limitedJson.RootElement.GetProperty("canLinkMembers").GetBoolean());
        Assert.Empty(limitedJson.RootElement.GetProperty("members").EnumerateArray());
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var full = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        using var fullJson = JsonDocument.Parse(await full.Content.ReadAsStringAsync());
        var fullItem = Assert.Single(fullJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(employee.Phone, fullItem.GetProperty("phone").GetString());
        Assert.Equal(operatorMember.Id, fullItem.GetProperty("memberId").GetGuid());
        Assert.True(fullJson.RootElement.GetProperty("canManage").GetBoolean());
        Assert.True(fullJson.RootElement.GetProperty("canLinkMembers").GetBoolean());
        Assert.Equal(3, fullJson.RootElement.GetProperty("members").GetArrayLength());
    }

    [Fact]
    public async Task SetupRoutesPersistDataEnforceIdentityAndReturnRevisionConflict()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.SetupAutoOpenPending = true;
        await context.SaveChangesAsync();
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        var route = $"/api/v1/organizations/{organization.Id}/setup";
        using var anonymous = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-User", "foreign");
        using var foreign = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var opened = await client.PostAsync(route + "/open", null);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var postId = Guid.NewGuid();
        using var saved = await client.PutAsJsonAsync(route,
            new { step = 0, revision = 0, items = new[] { new { id = postId, name = "Пост полировки" } } });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var conflict = await client.PutAsJsonAsync(route,
            new { step = 0, revision = 0, items = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var invalid = await client.PutAsJsonAsync(route, new { step = 9, revision = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var read = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var payload = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(1, payload.RootElement.GetProperty("revision").GetInt32());
        var post = await context.OrganizationScenarioCatalogItems.AsNoTracking().SingleAsync(value => value.Id == postId);
        Assert.Equal("Пост полировки", post.Name);
        Assert.Equal(organization.Id, post.OrganizationId);
        Assert.Equal("work_post", post.KindCode);
        Assert.Equal(1, await context.Organizations.AsNoTracking().Where(value => value.Id == organization.Id)
            .Select(value => value.SetupRevision).SingleAsync());
    }

    private IHost CreateHost(IFileStorageService? fileStorage = null)
    {
        var builder = new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
        {
            services.AddScoped<OpenCRMContext>(_ => fixture.CreateContext());
            services.AddScoped<IOrganizationSubscriptionService>(provider =>
            {
                var context = provider.GetRequiredService<OpenCRMContext>();
                var subscription = new Mock<IOrganizationSubscriptionService>();
                subscription.Setup(value => value.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                    .Returns<string, Guid, string>((userId, organizationId, _) => EnsureMemberAsync(userId, organizationId));
                subscription.Setup(value => value.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                    .Returns<string, Guid, string>((userId, organizationId, _) => EnsureMemberAsync(userId, organizationId));

                return subscription.Object;

                async Task EnsureMemberAsync(string userId, Guid organizationId)
                {
                    if (!await context.OrganizationMembers.AnyAsync(value => value.OrganizationId == organizationId && value.UserId == userId))
                    {
                        throw new InvalidOperationException("Нет доступа к организации.");
                    }
                }
            });
            services.AddOpenCRMCatalog();
            services.AddOpenCRMOrganizations();
            services.AddSingleton(fileStorage ?? Mock.Of<IFileStorageService>());
            services.AddRateLimiter(options => options.AddConcurrencyLimiter(
                RateLimitPolicyNames.OrganizationDataExport, limiter => limiter.PermitLimit = 1));
            services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
            services.AddSingleton(Mock.Of<IOrganizationInvitationCommandService>());
            services.AddAuthentication("OrganizationsTest")
                .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("OrganizationsTest", _ => { });
            services.AddAuthorization();
            services.AddControllersWithViews().AddApplicationPart(typeof(OrganizationSetupApiController).Assembly);
        }).Configure(application =>
        {
            application.UseRouting();
            application.UseAuthentication();
            application.UseRateLimiter();
            application.UseAuthorization();
            application.Use(async (context, next) =>
            {
                if (context.Request.Path == "/__test/antiforgery")
                {
                    var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                    var tokens = antiforgery.GetAndStoreTokens(context);
                    context.Response.Headers["X-Test-Antiforgery"] = tokens.RequestToken;
                    await context.Response.WriteAsync("ok");

                    return;
                }

                await next();
            });
            application.UseEndpoints(endpoints => endpoints.MapControllers());
        }));
        var host = builder.Build();
        host.Start();

        return host;
    }

    private static async Task AddAntiforgeryTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/__test/antiforgery");
        response.EnsureSuccessStatusCode();
        var requestToken = response.Headers.GetValues("X-Test-Antiforgery").Single();
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            client.DefaultRequestHeaders.Remove("Cookie");
            client.DefaultRequestHeaders.Add("Cookie", cookies.Single().Split(';')[0]);
        }
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", requestToken);
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

            var claimType = this.Request.Headers["X-Test-Identity-Claim"].FirstOrDefault() == "UserID"
                ? "UserID"
                : ClaimTypes.NameIdentifier;
            var identity = new ClaimsIdentity([new Claim(claimType, userId)], this.Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
