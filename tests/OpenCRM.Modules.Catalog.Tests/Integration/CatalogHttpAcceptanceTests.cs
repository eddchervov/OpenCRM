using OpenCRM.Modules.Planning.Contracts;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Catalog;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Controllers.Api.V1.Organizations;
using ISystemClock = OpenCRM.Domain.Interfaces.ISystemClock;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

[Collection(CatalogDatabaseCollection.Name)]
public sealed class CatalogHttpAcceptanceTests(CatalogDatabaseFixture fixture)
{
    [Fact]
    public async Task CatalogRoutesRequireAuthenticationOrganizationMembershipAndCatalogPermission()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context);
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        var route = $"/api/v1/organizations/{organization.Id}/scenario-catalog";

        using var anonymous = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var catalog = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Test-User");
        client.DefaultRequestHeaders.Add("X-Test-User", foreignOrganization.OwnerUserId);
        using var foreign = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        using var forbiddenHost = this.CreateHost(canViewCatalog: false);
        using var forbiddenClient = forbiddenHost.GetTestClient();
        forbiddenClient.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        using var forbidden = await forbiddenClient.GetAsync(route);
        Assert.Equal(HttpStatusCode.BadRequest, forbidden.StatusCode);
    }

    [Fact]
    public async Task CatalogPostAndPutRoutesPersistValidItemsAndRejectInvalidPrice()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        var route = $"/api/v1/organizations/{organization.Id}/scenario-catalog";

        using var invalid = await client.PostAsJsonAsync(route, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Name = "Цена с ошибкой",
            UnitPrice = -1
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.False(await context.OrganizationScenarioCatalogItems.AsNoTracking()
            .AnyAsync(item => item.OrganizationId == organization.Id && item.Name == "Цена с ошибкой"));

        using var created = await client.PostAsJsonAsync(route, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Name = "Полировка кузова",
            UnitPrice = 1200,
            DurationMinutes = 90
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var itemId = createdJson.RootElement.GetProperty("id").GetGuid();

        using var updated = await client.PutAsJsonAsync(route + $"/{itemId}", new UpdateOrganizationScenarioCatalogItemDto
        {
            Name = "Полировка кузова и фар",
            UnitPrice = 1500,
            DurationMinutes = 120,
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var saved = await context.OrganizationScenarioCatalogItems.AsNoTracking().SingleAsync(item => item.Id == itemId);
        Assert.Equal("Полировка кузова и фар", saved.Name);
        Assert.Equal(1500, saved.UnitPrice);
        Assert.Equal(120, saved.DurationMinutes);
    }

    [Fact]
    public async Task DeleteArchivesServicesPhysicallyDeletesUnusedPostsAndPreservesUsedPosts()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var service = this.CreateItem(organization.Id, OrganizationScenarioCatalogKindCodes.Service, "Услуга");
        var unusedPost = this.CreateItem(organization.Id, OrganizationScenarioCatalogKindCodes.WorkPost, "Свободный пост");
        var usedPost = this.CreateItem(organization.Id, OrganizationScenarioCatalogKindCodes.WorkPost, "Занятый пост");
        var vehicle = new Vehicle { User = organization.OwnerUser, Organization = organization };
        context.OrganizationScenarioCatalogItems.AddRange(service, unusedPost, usedPost);
        context.Maintenance.Add(new OpenCRM.Domain.Maintenance
        {
            User = organization.OwnerUser,
            Vehicle = vehicle,
            Location = usedPost.Name,
            Date = DateTime.UtcNow,
            Details = []
        });
        await context.SaveChangesAsync();

        using var host = this.CreateHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", organization.OwnerUserId);
        var route = $"/api/v1/organizations/{organization.Id}/scenario-catalog";

        using var archived = await client.DeleteAsync(route + $"/{service.Id}");
        Assert.Equal(HttpStatusCode.NoContent, archived.StatusCode);
        using var deleted = await client.DeleteAsync(route + $"/{unusedPost.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var auditCount = await context.OrganizationAuditEvents.CountAsync(item => item.OrganizationId == organization.Id);

        using var rejectedUpdate = await client.PutAsJsonAsync(route + $"/{usedPost.Id}", new UpdateOrganizationScenarioCatalogItemDto
        {
            Name = "Переименованный пост",
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejectedUpdate.StatusCode);
        using var archivedPost = await client.DeleteAsync(route + $"/{usedPost.Id}");
        Assert.Equal(HttpStatusCode.NoContent, archivedPost.StatusCode);

        await using var verificationContext = fixture.CreateContext();
        Assert.False(await verificationContext.OrganizationScenarioCatalogItems.AnyAsync(item => item.Id == unusedPost.Id));
        Assert.False((await verificationContext.OrganizationScenarioCatalogItems.SingleAsync(item => item.Id == service.Id)).IsActive);
        var preservedPost = await verificationContext.OrganizationScenarioCatalogItems.SingleAsync(item => item.Id == usedPost.Id);
        Assert.Equal("Занятый пост", preservedPost.Name);
        Assert.False(preservedPost.IsActive);
        Assert.True(preservedPost.HasBeenUsed);
        Assert.Equal(auditCount + 1, await verificationContext.OrganizationAuditEvents.CountAsync(item => item.OrganizationId == organization.Id));

        using var reactivatedPost = await client.PutAsJsonAsync(route + $"/{usedPost.Id}", new UpdateOrganizationScenarioCatalogItemDto
        {
            Name = "Занятый пост",
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.OK, reactivatedPost.StatusCode);
        var restoredPost = await verificationContext.OrganizationScenarioCatalogItems.AsNoTracking()
            .SingleAsync(item => item.Id == usedPost.Id);
        Assert.True(restoredPost.IsActive);
        Assert.True(restoredPost.HasBeenUsed);
        Assert.Equal("Занятый пост", restoredPost.Name);
        Assert.Equal(auditCount + 2, await verificationContext.OrganizationAuditEvents.CountAsync(item => item.OrganizationId == organization.Id));
    }

    private IHost CreateHost(bool canViewCatalog = true)
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddScoped<OpenCRMContext>(_ => fixture.CreateContext());
                services.AddScoped<UserManager<ApplicationUser>>(provider =>
                {
                    var context = provider.GetRequiredService<OpenCRMContext>();
                    var manager = new Mock<UserManager<ApplicationUser>>(
                        Mock.Of<IUserStore<ApplicationUser>>(),
                        Options.Create(new IdentityOptions()),
                        Mock.Of<IPasswordHasher<ApplicationUser>>(),
                        Array.Empty<IUserValidator<ApplicationUser>>(),
                        Array.Empty<IPasswordValidator<ApplicationUser>>(),
                        Mock.Of<ILookupNormalizer>(),
                        new IdentityErrorDescriber(),
                        Mock.Of<IServiceProvider>(),
                        Mock.Of<ILogger<UserManager<ApplicationUser>>>());
                    manager.Setup(value => value.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
                        .Returns<ClaimsPrincipal>(principal =>
                        {
                            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                            return userId == null
                                ? Task.FromResult<ApplicationUser?>(null)
                                : context.Users.SingleOrDefaultAsync(user => user.Id == userId);
                        });

                    return manager.Object;
                });
                services.AddScoped<IOrganizationQueryService>(provider =>
                {
                    var context = provider.GetRequiredService<OpenCRMContext>();
                    var query = new Mock<IOrganizationQueryService>();
                    query.Setup(value => value.GetOverviewAsync(It.IsAny<string>()))
                        .Returns<string>(GetOverviewAsync);

                    return query.Object;

                    async Task<TeamOverviewDto> GetOverviewAsync(string userId)
                    {
                        var organizations = await context.Organizations.AsNoTracking()
                            .Where(organization => organization.Members.Any(member => member.UserId == userId)
                                && !organization.IsDeleted && !organization.IsArchived && organization.IsActive)
                            .Select(organization => new OrganizationDto { Id = organization.Id, Name = organization.Name })
                            .ToListAsync();

                        return new TeamOverviewDto { Organizations = organizations };
                    }
                });
                services.AddScoped<IOrganizationSubscriptionCheckoutService>(_ => Mock.Of<IOrganizationSubscriptionCheckoutService>());
                services.AddScoped<IOrganizationSubscriptionService>(provider =>
                {
                    var context = provider.GetRequiredService<OpenCRMContext>();
                    var subscription = new Mock<IOrganizationSubscriptionService>();
                    subscription.Setup(value => value.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                        .Returns<string, Guid, string>((userId, organizationId, _) => EnsureMembershipAsync(userId, organizationId));
                    subscription.Setup(value => value.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                        .Returns<string, Guid>(EnsureMembershipAsync);
                    subscription.Setup(value => value.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                        .Returns<string, Guid, string>((userId, organizationId, _) => EnsureMembershipAsync(userId, organizationId));

                    return subscription.Object;

                    async Task EnsureMembershipAsync(string userId, Guid organizationId)
                    {
                        if (!await context.OrganizationMembers.AsNoTracking().AnyAsync(member =>
                            member.OrganizationId == organizationId && member.UserId == userId))
                        {
                            throw new InvalidOperationException("Нет доступа к организации.");
                        }
                    }
                });
                var permissions = new Mock<IOrganizationPermissionService>();
                permissions.Setup(value => value.HasPermissionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                    .ReturnsAsync(canViewCatalog);
                services.AddSingleton(permissions.Object);
                services.AddSingleton(Mock.Of<IOrganizationProfileCommandService>());
                services.AddSingleton(Mock.Of<IOrganizationArchiveService>());
                services.AddSingleton(Mock.Of<IOrganizationMembershipService>());
                services.AddSingleton(Mock.Of<IOrganizationInvitationCommandService>());
                services.AddSingleton(Mock.Of<IWorkspaceOverviewService>());
                services.AddSingleton(Mock.Of<IOrganizationWorkspaceShellService>());
                services.AddSingleton(Mock.Of<IOrganizationWorkspaceQueryService>());
                services.AddSingleton(Mock.Of<IVehicleOptionQueryService>());
                services.AddSingleton(Mock.Of<ISchedulerQueryService>());
                services.AddSingleton(Mock.Of<ISchedulerTaskCommandService>());
                services.AddSingleton(Mock.Of<ISchedulerRecurrenceService>());
                services.AddSingleton(Mock.Of<IInventoryQueryService>());
                services.AddSingleton(Mock.Of<IStockCommandService>());
                services.AddSingleton(Mock.Of<ISystemClock>());
                // Справка организации вынесена из платформы: контроллер рабочего пространства получает статьи справки
                // через этот порт, в приложении его регистрирует слой представления (пустая реализация без статей).
                services.AddSingleton(Mock.Of<OpenCRM.WebApp.Models.Organizations.IOrganizationWikiContentProvider>());
                services.AddOpenCRMCatalog();
                services.AddAuthentication("CatalogTest")
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>("CatalogTest", _ => { });
                services.AddAuthorization();
                services.AddControllers().AddApplicationPart(typeof(OrganizationWorkspaceApiController).Assembly);
            });
            web.Configure(application =>
            {
                application.UseRouting();
                application.UseAuthentication();
                application.UseAuthorization();
                application.UseEndpoints(endpoints => endpoints.MapControllers());
            });
        });
        var host = builder.Build();
        host.Start();

        return host;
    }

    private OrganizationScenarioCatalogItem CreateItem(Guid organizationId, string kindCode, string name) => new()
    {
        OrganizationId = organizationId,
        ScenarioCode = OrganizationTypeCodes.Detailing,
        KindCode = kindCode,
        Code = "test-" + Guid.NewGuid().ToString("N"),
        Name = name
    };

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
